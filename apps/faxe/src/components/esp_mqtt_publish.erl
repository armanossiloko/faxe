%%%-------------------------------------------------------------------
%%% @author heyoka
%%% @copyright (C) 2019, <COMPANY>
%%% @doc
%%% Publish every single message to a mqtt-broker.
%%% Incoming data_points or data_batches are converted to a JSON string before sending.
%%% If the safe() parameter is given, every message first gets stored in an on-disk queue before it will
%%% be sent, this way we can make sure no message gets lost when disconnected from the broker.
%%% Note that in safe mode, messages are not sent right away, so a delay of up to a second may be introduced.
%%% other params:
%%% port() is 1883 by default
%%% qos() is 1 by default
%%% ssl() is false by default
%%% retained() is false by default
%%% @end
%%% Created : 27. May 2019 09:00
%%%-------------------------------------------------------------------
-module(esp_mqtt_publish).
-author("heyoka").

%% API
-behavior(df_component).

-include("faxe.hrl").
%% API
-export([init/3, process/3, options/0, handle_info/2, shutdown/1, metrics/0, check_options/0]).

-define(META_FIELD, <<"_meta">>).

%% state for safe-mode
-record(state, {
   publisher,
   options,
   queue_file,
   queue,
   mem_queue, %:: memory_queue:memory_queue(),
   topic,
   topic_lambda,
   topic_field,
   safe = false,
   fn_id,
   debug_mode = false,
   use_pool = false :: true|false,
   pool_connected = false :: true|false,
   pool_key :: tuple(),
   delete_mode = false, %% if true, an empty message will be published instead of the actual message, this leads to del topic (retained)
%%   seq_num = 1,
   add_seq_check = true,
   seq_check_topic_depth = 5,
   seq_threshold,
   seq_counters = #{},

   meta_fields = #{}
}).
%% state for direct publish mode

options() -> [
   {host, binary, {mqtt, host}},
   {port, integer, {mqtt, port}},
   {user, string, {mqtt, user}},
   {pass, string, {mqtt, pass}},
   {client_id, string, undefined},
   {qos, integer, 1},
   {topic, binary, undefined},
   {topic_field, binary, undefined},
   {topic_lambda, lambda, undefined},
   {retained, is_set},
   {ssl, is_set, {mqtt, ssl, enable}},
   {safe, boolean, false},
   {max_mem_queue_size, integer, 300},
   {use_pool, boolean, {mqtt_pub_pool, enable}},
   %% experimental delete mode
   {'_delete', boolean, false},
   %% seq_check
   {add_seq_check, boolean, {seq_check, enable}},
   {seq_check_topic_depth, integer, {seq_check, topic_depth}}
].

check_options() ->
   [
      {one_of_params, [topic, topic_lambda, topic_field]},
      {func, topic,
         fun
            (undefined) -> true;
            (T) -> faxe_util:check_publisher_mqtt_topic(T)
         end, <<": ">>}
   ].

metrics() ->
   [
%%      {?METRIC_SENDING_TIME, histogram, [slide, 60], "Network time for sending a message."},
      {?METRIC_BYTES_SENT, meter, [], "Size of item sent in kib."}
   ].

%% safe mode with ondisc queuing
init({_GraphId, _NodeId} = GId, _Ins, #{safe := true}=Opts) ->
   QFile = faxe_config:q_file(GId),
   QConf = proplists:delete(ttf, faxe_config:get_esq_opts()),
   {ok, Q} = esq:new(QFile, QConf),
   NewOpts = prepare_opts(GId, Opts),
   {ok, Publisher} = mqtt_publisher:start_link(NewOpts, Q),
   init_all(NewOpts, #state{publisher = Publisher, queue = Q, fn_id = GId});
%% direct publish mode
init(NodeId, _Ins, #{use_pool := true, max_mem_queue_size := MemSize} = Opts) ->
   NewOpts = prepare_opts(NodeId, Opts),
%%   lager:info("use mqtt_pub_pool with opts: ~p",[NewOpts]),
   MqttPool = mqtt_pub_pool_manager:connect(NewOpts),
   init_all(NewOpts, #state{fn_id = NodeId, mem_queue = memory_queue:new(MemSize), pool_key = MqttPool});
init(NodeId, _Ins, #{safe := false} = Opts) ->
   NewOpts = prepare_opts(NodeId, Opts),
   {ok, Publisher} = mqtt_publisher:start_link(NewOpts),
   init_all(NewOpts, #state{publisher = Publisher, fn_id = NodeId}).

init_all(
    #{safe := Safe, topic := Topic, topic_lambda := LTopic, topic_field := TField,
       use_pool := Pool, host := Host, port := Port, '_delete' := Delete,
       add_seq_check := AddCheck, seq_check_topic_depth := SeqCheckTopicDepth} = Opts,
    State = #state{fn_id = {FlowId, NodeId} =NId}) ->

   %% when using the connection pool, we have to take care of the connection_registry ourselves
   case Pool of
      true -> connection_registry:reg(NId, Host, Port, <<"mqtt">>);
      false -> ok
   end,
   Meta = #{<<"flowid">> => FlowId, <<"nodeid">> => NodeId, <<"device">> => faxe_util:device_name()},
   {ok, all,
      State#state{
         options = Opts, safe = Safe, topic = Topic, topic_lambda = LTopic, topic_field = TField, use_pool = Pool,
         delete_mode = Delete, meta_fields = Meta, seq_threshold = faxe_config:get_sub(seq_check, max_seq_num, 9999),
         seq_check_topic_depth = SeqCheckTopicDepth, add_seq_check = AddCheck}
   }.

prepare_opts({GId, NId}=GNId, Opts0 = #{client_id := CId, host := Host0, '_delete' := DelMode, retained := Ret}) ->
   Host = binary_to_list(Host0),
   ClientId = case CId of undefined -> <<GId/binary, "_", NId/binary>>; _ -> CId end,
   Retained = case DelMode of true -> true; _ -> Ret end,
   Opts0#{host => Host, client_id => ClientId, node_id => GNId, retained => Retained}.

%% safe state
process(_In, Item, State = #state{safe = true, queue = Q, fn_id = FNId}) ->
   {Topic, Item1, NewState} = build_item(Item, State),
   ok = esq:enq(build_message(Item1, Topic, NewState), Q),
   dataflow:maybe_debug(item_out, 1, Item1, FNId, NewState#state.debug_mode),
   {ok, NewState};
%% using the connection pool
process(_Inport, Item,
    State = #state{use_pool = true, pool_connected = false, mem_queue = MemQ,
       options = #{qos := Qos, retained := Ret}}) ->
   {Topic, Message} = build_message(Item, State),
   M = {publish, {Topic, Message, Qos, Ret}},
%%   lager:info("mem queue msg, because pool not connected ~p",[M]),
   NewMemQ = memory_queue:enq(M, MemQ),
   {ok, State#state{mem_queue = NewMemQ}};
process(_Inport, Item, State = #state{safe = false, use_pool = true, fn_id = FNId, pool_key = Key,
      options = #{qos := Qos, retained := Ret}}) ->
%%   lager:alert("~p got item ~p",[?MODULE, Item]),
   {ok, Publisher} = mqtt_pub_pool_manager:get_connection(Key),
   {Topic, Item1, NewState} = build_item(Item, State),
   {Topic, Message} = build_message(Item1, Topic, NewState),
   Publisher ! {publish, {Topic, Message, Qos, Ret}},
   dataflow:maybe_debug(item_out, 1, Item1, FNId, NewState#state.debug_mode),
   {ok, NewState};
process(_Inport, Item, State = #state{safe = false, publisher = Publisher, fn_id = FNId}) ->
%%   lager:warning("send msg when not safe and no pool used: ~p",[lager:pr(State, ?MODULE)]),
   {Topic, Item1, NewState} = build_item(Item, State),
   TopicMessage = build_message(Item1, Topic, NewState),
   Publisher ! {publish, TopicMessage},
   dataflow:maybe_debug(item_out, 1, Item1, FNId, NewState#state.debug_mode),
   {ok, NewState}.

%% we only get these, when pool is used
handle_info({mqtt_connected, _}, State = #state{mem_queue = Q, pool_key = Key}) ->
   lager:info("mqtt_pool CONNECTED, resend ~p",[memory_queue:to_list(Q)]),
   {PendingList, NewQ} = memory_queue:to_list_reset(Q),
   case PendingList of
      [] -> ok;
      L when is_list(L) ->
         lager:info(ets:tab2list(mqtt_pub_pools)),
         {ok, Publisher} = mqtt_pub_pool_manager:get_connection(Key),
         [Publisher ! M || M <- PendingList]
   end,
   connection_registry:connected(),
   {ok, State#state{pool_connected = true, mem_queue = NewQ}};
handle_info({mqtt_disconnected, _}, State) ->
   lager:info("mqtt_pool DISCONNECTED"),
   connection_registry:disconnected(),
   {ok, State#state{pool_connected = false}};

handle_info(start_debug, State) -> {ok, State#state{debug_mode = true}};
handle_info(stop_debug, State) -> {ok, State#state{debug_mode = false}};
handle_info(_E, S) ->
   {ok, S}.

shutdown(#state{publisher = P}) ->
   catch gen_server:stop(P).

build_item(Item, State) ->
   Topic = get_topic(Item, State),
   {Item1, NewState} = maybe_add_meta(Item, Topic, State),
   {Topic, Item1, NewState}.

build_message(_Item, Topic, #state{fn_id = _FNId, delete_mode = true}) ->
   {Topic, <<>>};
build_message(Item, Topic, #state{fn_id = FNId}) ->
   Json = flowdata:to_json(Item),
%%   node_metrics:metric(?METRIC_BYTES_SENT, byte_size(Json), FNId),
   node_metrics:metric(?METRIC_ITEMS_OUT, 1, FNId),
   {Topic, Json}.

build_message(Item, State = #state{fn_id = _FNId, delete_mode = true}) ->
   {get_topic(Item, State), <<>>};
build_message(Item, State) ->
   {Topic, Item1, NewState} = build_item(Item, State),
   build_message(Item1, Topic, NewState).


maybe_add_meta(Item = #data_point{fields = Fields}, Topic,
    State = #state{add_seq_check = true, meta_fields = Meta0, seq_threshold = Threshold}) ->
   {MetaTopic, IsFreshStart, NewState}  = get_seq_counter(Topic, State),
   Pos = 2, Inc = 1, SetValue = 1,
   Seq = ets:update_counter(mqtt_seq_cnt, MetaTopic, {Pos, Inc, Threshold, SetValue}, {MetaTopic, 0}),
   NewFields = Fields#{?META_FIELD => Meta0#{<<"seq">> => Seq, <<"topic">> => MetaTopic, <<"started">> => IsFreshStart}},
   {Item#data_point{fields = NewFields}, NewState};
maybe_add_meta(Item , _T, State) ->
   {Item, State}.

get_seq_counter(Topic, State = #state{seq_counters = Counts}) when is_map_key(Topic, Counts) ->
   {maps:get(Topic, Counts), false, State};
get_seq_counter(Topic, State = #state{seq_check_topic_depth = Depth, seq_counters = Counts}) ->
   MetaTopic = faxe_util:subtopic(Topic, Depth),
   IsFreshStart = case ets:lookup(mqtt_seq_cnt, MetaTopic) of [] -> true; _ -> false end,
   {MetaTopic, IsFreshStart, State#state{seq_counters = Counts#{Topic => MetaTopic}}}.

get_topic(_Item, # state{topic_lambda = undefined, topic_field = undefined, topic = Topic}) ->
   Topic;
get_topic(Item = #data_point{}, # state{topic_lambda = undefined, topic = undefined, topic_field = TField}) ->
   flowdata:field(Item, TField);
get_topic(#data_batch{points = [P|_]}, # state{topic_lambda = undefined, topic = undefined, topic_field = TField}) ->
   flowdata:field(P, TField);
get_topic(#data_point{} = P, #state{topic_lambda = Fun}) ->
   faxe_lambda:execute(P, Fun);
get_topic(#data_batch{points = [P1|_]}, #state{topic_lambda = Fun}) ->
   faxe_lambda:execute(P1, Fun).


