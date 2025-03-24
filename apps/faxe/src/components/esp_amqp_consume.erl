%%%-------------------------------------------------------------------
%%% @author heyoka
%%% @copyright (C) 2019, <COMPANY>
%%% @doc
%%% Consume data from an amqp-broker like rabbitmq.
%%% If safe is true -> use internal on disc queue, otherwise just emit to downstream nodes
%%%
%%% @end
%%% Created : 27. May 2019 09:00
%%%-------------------------------------------------------------------
-module(esp_amqp_consume).
-author("heyoka").

%% API
-behavior(df_component).

-include("faxe.hrl").
%% API
-export([
   init/3,
   process/3,
   options/0,
   handle_info/2,
   shutdown/1,
   metrics/0,
   check_options/0,
   handle_ack/3]).

-define(VHOST_DEFAULT, <<"/">>).
-define(QUEUE_TYPES, [<<"classic">>, <<"quorum">>, <<>>]).

%% state for direct publish mode
-record(state, {
   consumer,
   host,
   port,
   user,
   pass,
   ssl = false,
   vhost,
   esq_queue,
   queue_name,
   queue_type,
   takeover_queue_name,
   takeover_queue_type,
   %% pid of takeover_consumer, that is started from within this node
   takeover_consumer_pid,
   takeover_consumer_opts,
   exchange,
   root_exchange,
   routing_key = false,
   bindings = false,
   prefetch,
   collected = 0,
   ack_every,
   ack_after,
   ack_timer,
   flow_ack,
   last_dtag,

   opts,
   dt_field,
   dt_format,
   clean_names,
   emitter,
   flownodeid,
   debug_mode = false,
   include_topic = true,
   topic_key,
   as,
   safe_mode = false,
   confirm = true,
   dedup_queue :: memory_queue:memory_queue(),
   %% know which channel gave us the DTAGs so far
   last_chan = undefined,
   %% takeover parent
   parent_pid,
   parent_subscriptions = [],
   %% queue declare passive mode
   passive,
   takeover_time,
   takeover_timer,
   takeover_data :: memory_queue:memory_queue()
}).

options() -> [
   {host, binary, {amqp, host}},
   {port, integer, {amqp, port}},
   {user, string, {amqp, user}},
   {pass, string, {amqp, pass}},
   {ssl, is_set, false},
   {vhost, string, <<"/">>},
   %% only applies, if vhost is NOT the default: '/', applies to both vhost params
   {vhost_prefix, string, {rabbitmq, vhost_prefix}},
   {routing_key, string, undefined},
   {bindings, string_list, undefined},
   {qx_name, string, undefined}, %% not used currently
   {queue, any, undefined},
   {queue_type, string, {rabbitmq, queue_type}},
   %%%%%%%%%%%%%%% queue takeover options:
%%   {takeover_host, binary, {amqp, host}},
%%   {takeover_port, integer, {amqp, port}},
%%   {takeover_user, string, {amqp, user}},
%%   {takeover_pass, string, {amqp, pass}},
%%   {takeover_ssl, is_set, false},

   {takeover, boolean, {rabbitmq, takeover}},
   {takeover_timeout, duration, <<"5m">>},
   {takeover_queue, string, undefined},
   {takeover_queue_prefix, string, {rabbitmq, takeover_queue_prefix}},
   {takeover_queue_type, string, {rabbitmq, takeover_queue_type}},
   %% defaults to "vhost"
   {takeover_queue_vhost, string, undefined},
   %%%%%%%%%%%%%%%%
   {queue_prefix, string, {rabbitmq, queue_prefix}},
   {consumer_tag, string, undefined},
   {exchange, string, undefined},
   {root_exchange, string, undefined},
   {exchange_prefix, string, {rabbitmq, exchange_prefix}},
   {prefetch, integer, 70},
   {ack_every, integer, 30},
   {ack_after, duration, <<"5s">>},
   {use_flow_ack, bool, {amqp, flow_ack, enable}},
   {safe, boolean, false},
   {dt_field, string, <<"ts">>},
   {dt_format, string, ?TF_TS_MILLI},
   {clean_field_names, boolean, false},
   {include_topic, bool, true},
   {topic_as, string, <<"topic">>},
   {as, string, undefined},
   {confirm, boolean, true},
   {dedup_size, integer, 350},
   {'_parent_pid', any, undefined},
   {'_parent_subscriptions', any, undefined},
   {passive, bool, false}
].

check_options() ->
   [
      {one_of_params, [routing_key, bindings]},
      {one_of, queue_type, ?QUEUE_TYPES},
      {one_of, takeover_queue_type, ?QUEUE_TYPES}
   ].

metrics() ->
   [
      {?METRIC_BYTES_READ, meter, []}
   ].

init({GraphId, NodeId} = Idx, _Ins,
   #{ host := Host0, port := Port, user := _User, pass := _Pass, vhost := VHost0, queue := Q0, queue_type := QType0,
      exchange := Ex0, qx_name := _QxName, prefetch := Prefetch, routing_key := RoutingKey0, bindings := Bindings0,
      dt_field := DTField, dt_format := DTFormat, ssl := UseSSL, include_topic := IncludeTopic, vhost_prefix := VHostPrefix,
      topic_as := TopicKey, ack_every := AckEvery0, ack_after := AckTimeout0, as := As, consumer_tag := CTag0,
      queue_prefix := QPrefix, root_exchange := RExchange, exchange_prefix := XPrefix
      , use_flow_ack := FlowAck, clean_field_names := Clean,
   safe := Safe, confirm := Confirm, dedup_size := DedupSize,
      takeover := Takeover, takeover_timeout := TakeoverTimeout,
      takeover_queue := TakeoverQ0, takeover_queue_prefix := TakeoverQPrefix, takeover_queue_type := TakeoverQType0,
      takeover_queue_vhost := _TakeoverVHost,
      '_parent_pid' := ParentPid, '_parent_subscriptions' := ParentSubs, passive := Passive
   } = Opts0) ->

   VHost = case VHost0 of ?VHOST_DEFAULT -> VHost0; _ -> faxe_util:prefix_binary(VHost0, VHostPrefix) end,
%%   lager:warning("opts ~p", [Opts0]),
   Q = eval_name(Q0, Opts0, Idx),
   QName = faxe_util:prefix_binary(Q, QPrefix),
   QType = faxe_util:to_list(QType0),
   CTag = case CTag0 of undefined -> <<"c_", GraphId/binary, "_", NodeId/binary>>; _ -> CTag0 end,
   State0 = #state{
      include_topic = IncludeTopic, topic_key = TopicKey, as = As, dedup_queue = memory_queue:new(DedupSize),
      prefetch = Prefetch, ack_every = AckEvery0, flow_ack = FlowAck,
      dt_field = DTField, dt_format = DTFormat, safe_mode = Safe, flownodeid = Idx, confirm = Confirm,
      clean_names = Clean, ssl = UseSSL, queue_type = QType,
      parent_pid = ParentPid,
      parent_subscriptions = ParentSubs, passive = Passive},
   %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
   %% in case of takeover
   {TakeoverOpts, State1} =
   case Takeover of
      false -> {undefined, State0};
      true ->
         TakeoverQ1 = eval_name(TakeoverQ0, Opts0, Idx),
         TakeoverQ2 = faxe_util:prefix_binary(TakeoverQ1, TakeoverQPrefix),

         TakeoverTime = faxe_time:duration_to_ms(TakeoverTimeout),
         NewTOpts = Opts0#{takeover_queue => TakeoverQ2, takeover_time => TakeoverTime},
         TakeoverOpts0 = init_takeover_consumer(self(), Idx, CTag, NewTOpts),
         CompQ = #{queue => QName, queue_type => QType, vhost => VHost},
         case check_unique_q(TakeoverOpts0, CompQ) of
            true ->
               %% start takeover consumer
               State01 = State0#state{takeover_consumer_opts = TakeoverOpts0},
               TakeoverPid = start_takeover_consumer(State01),
               TakeoverData = memory_queue:new(DedupSize),
               {TakeoverOpts0, State01#state{takeover_consumer_pid = TakeoverPid, takeover_data = TakeoverData}};
            false ->
               lager:warning(
                  "cannot start takeover action, because both queues are the same ~p, will continue without takeover",
                  [CompQ]),
               {undefined, State0}
         end
   end,

   TakeoverQType = faxe_util:to_list(TakeoverQType0),
   TakeoverQ = case is_map(TakeoverOpts) andalso is_map_key(queue, TakeoverOpts) of
                  true -> maps:get(queue, TakeoverOpts);
                  false -> undefined
               end,
%%   lager:notice("takeover_q opts ~p",[TakeoverOpts]),
   %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
   Ex = eval_name(Ex0, Opts0, Idx),
   process_flag(trap_exit, true),
   AckTimeout = faxe_time:duration_to_ms(AckTimeout0),

   Host = binary_to_list(Host0),

   Opts = Opts0#{
      host => Host, consumer_tag => CTag, vhost => VHost,
      exchange => faxe_util:prefix_binary(Ex, XPrefix),
      root_exchange => case RExchange of undefined -> undefined; _ -> RExchange end,
      queue => QName, queue_type => QType,
      routing_key => faxe_util:to_rkey(RoutingKey0),
      bindings => faxe_util:to_rkey(Bindings0)
   },
%%   lager:info("opts: ~p",[Opts]),

   State = State1#state{
      opts = Opts, ack_after = AckTimeout, queue_type = QType,
      takeover_queue_name = TakeoverQ, queue_name = QName,
      takeover_queue_type = TakeoverQType, takeover_time = maps:get(takeover_time, Opts, undefined)},

   NewState = maybe_init_q(State),
   %% connection reg only when parent is present -> we are the takeover consumer
   case is_pid(State#state.parent_pid) of
      false -> connection_registry:reg(Idx, Host, Port, <<"amqp">>);
      true -> ok
   end,

   {ok, start_consumer(NewState)}.

check_unique_q(#{queue := Q, queue_type := QType, vhost := VHost}, #{queue := Q, queue_type := QType, vhost := VHost}) ->
   false;
check_unique_q(_, _) ->
   true.


init_takeover_consumer(ParentPid, IdxParent, CTag,
    Opts = #{takeover_queue := Q0, takeover_queue_type := QType0, '_name' := Name, takeover_queue_vhost := TVHost,
       vhost := VHost, vhost_prefix := VHostPrefix}) ->
   TakeoverVHost0 = case TVHost of undefined -> VHost; _ -> TVHost end,
   TakeoverVHost =
      case TakeoverVHost0 of
         ?VHOST_DEFAULT -> TakeoverVHost0;
         _ -> faxe_util:prefix_binary(TakeoverVHost0, VHostPrefix)
      end,
   NewOpts = Opts#{
      %% do not use esq for takeover
      safe => false,
      %% also do not use flow_ack, as downstream nodes won't be able to distinguish between nodes anymore
      use_flow_ack => false,
      %% for the takeover-consumer, the takeover options become the "normal" q opts
      queue => Q0,
      queue_type => faxe_util:to_list(QType0),
      vhost => TakeoverVHost,
      %% cannot use queue prefix
      queue_prefix => <<>>,
      %% and of course, no takeover queue for the takeover consumer
      %% and also the flag will be set to false, otherwise we will run into a loop in 'init'
      takeover_queue => undefined,
      takeover => false,
      consumer_tag => <<CTag/binary, "_takeover_consumer">>,
      '_name' => <<Name/binary, "_takeover">>,
      '_parent_pid' => ParentPid,
      '_parent_subscriptions' => df_subscription:subscriptions(IdxParent),
      '_stop_idle' => false,
      passive => true % should be "true" for takeover to just happen once
   },
   NewOpts.


-spec start_takeover_consumer(State :: #state{}) -> pid().
start_takeover_consumer(#state{flownodeid = {GraphId, _NodeId}, takeover_consumer_opts = Opts = #{'_name' := Name}}) ->
   {ok, Pid} =
      df_component:start_link(
         ?MODULE,
         GraphId,
         Name,
         [{1,nil}],
         [{1,nil}],
         Opts),
   Pid ! {start, [], push},
   Pid.

check_takeover_data(#state{takeover_data = undefined}, _Corr) ->
   false;
check_takeover_data(#state{takeover_data = TData}, CorrId) ->
   memory_queue:member(CorrId, TData).

maybe_init_q(State = #state{safe_mode = false}) ->
   State;
maybe_init_q(State = #state{flownodeid = Idx}) ->
   QFile = faxe_config:q_file(Idx),
   QConf = proplists:delete(ttf, faxe_config:get_esq_opts()),
   {ok, Q} = esq:new(QFile, QConf),
   start_emitter(State#state{esq_queue = Q}).

process(_In, _, State = #state{}) ->
   {ok, State}.

%%
%% new queue-message arrives ...
%%
handle_info({deliver, _QueueName, Channel, {DTag, RKey}, {Payload, CorrelationId, _Headers}},
    State=#state{flownodeid = FNId, dedup_queue = Dedup, last_chan = _OldChannel, parent_pid = Parent}) ->

   node_metrics:metric(?METRIC_BYTES_READ, byte_size(Payload), FNId),
   node_metrics:metric(?METRIC_ITEMS_IN, 1, FNId),
   TPid = State#state.takeover_consumer_pid,
   State1 =
   case is_pid(TPid) andalso check_takeover_data(State, CorrelationId) of
      true ->
         TPid ! tookover,
         TPid ! stop,
         State#state{takeover_consumer_pid = undefined, takeover_data = undefined};
      false -> State
   end,
   NewState = maybe_takeover_timeout(State1#state{last_chan = Channel}),
   case
      State1#state.flow_ack /= true andalso
         CorrelationId /= undefined andalso
         memory_queue:member(CorrelationId, Dedup)
   of
      true ->
         lager:info("duplicate message found! [~p]",[CorrelationId]),
         {ok, maybe_ack(DTag, NewState)};
      false ->
         %% store correlation_id, if we do not use flow_ack and also have no takeover consumer running
         NewDedup =
         case State1#state.flow_ack andalso not is_pid(TPid) of
            true -> Dedup;
            false -> memory_queue:enq(CorrelationId, Dedup)
         end,
         Item0 = build_item(Payload, RKey, NewState),
         Item = case is_pid(Parent) of
                   true -> Item0; %% dtag field is undefined at this point, we want that for the takeover consumer
                   false -> flowdata:set_dtag(Item0, DTag)
                end,
         dataflow:maybe_debug(item_in, 1, Item, FNId, NewState#state.debug_mode),
         enq_or_emit(Item, CorrelationId, DTag, NewState#state{dedup_queue = NewDedup})
   end;

handle_info({amqp_connected, Consumer}, #state{consumer = Consumer} = State) ->
   connection_registry:connected(),
   {ok, maybe_takeover_timeout(State)};
handle_info({amqp_disconnected, Consumer}, #state{consumer = Consumer} = State) ->
   connection_registry:disconnected(),
   {ok, cancel_takeover_timeout(State)};
handle_info({'DOWN', _MonitorRef, process, Consumer, {{shutdown, {server_initiated_close, 404, Msg}}, _GenCall} = Info},
      #state{consumer = Consumer, queue_name = QName, passive = true, parent_pid = Parent} = State) when is_pid(Parent)
   ->
   % NOT_FOUND - no queue
%%   connection_registry:disconnected(),
   case estr:str_contains(Msg, <<"NOT_FOUND - no queue">>) of
      true -> lager:notice("MQ-Consumer ~p is 'DOWN' because takeover-queue ~p could not be found (passive mode)",
         [Consumer, QName]);
      false ->
         lager:notice("MQ-Consumer ~p is 'DOWN' with 404 but unclear reason :: ~p",[Consumer, Info])
   end,
   Parent ! takeover_queue_done,
   {ok, State};
handle_info({'EXIT', TakeoverConsumer, normal}, #state{takeover_consumer_pid = TakeoverConsumer} = State) ->
   {ok, State#state{takeover_consumer_pid = undefined}};
handle_info({'EXIT', TakeoverConsumer, Reason}, #state{takeover_consumer_pid = TakeoverConsumer} = State) ->
   lager:notice("Takeover consumer exited with reason ~p, will restart",[Reason]),
   TakeoverConsumerPid = start_takeover_consumer(State),
   {ok, State#state{takeover_consumer_pid = TakeoverConsumerPid}};
handle_info({'DOWN', _MonitorRef, process, Consumer, Info}, #state{consumer = Consumer} = State) ->
   connection_registry:disconnected(),
   lager:notice("MQ-Consumer ~p is 'DOWN' for reason ~p",[Consumer, Info]),
   {ok, start_consumer(cancel_takeover_timeout(State))};
handle_info({'DOWN', _MonitorRef, process, Emitter, _Info}, #state{emitter = Emitter} = State) ->
   lager:notice("Q-Emitter ~p is 'DOWN'",[Emitter]),
   {ok, start_emitter(State)};
handle_info(ack_timeout, State = #state{last_dtag = undefined}) ->
   {ok, State#state{ack_timer = undefined}};
handle_info(ack_timeout, State = #state{collected = _Num}) ->
   NewState = do_ack(State),
   {ok, NewState};
handle_info(start_debug, State) -> {ok, State#state{debug_mode = true}};
handle_info(stop_debug, State) -> {ok, State#state{debug_mode = false}};
handle_info(takeover_queue_done, State=#state{takeover_consumer_pid = TPid}) ->
   lager:notice("takeover done!"),
   catch TPid ! stop,
   {ok, State#state{takeover_consumer_pid = undefined, takeover_data = undefined}};
handle_info({takeover_data, CorrelationId}, State=#state{dedup_queue = Dedup, takeover_consumer_pid = TPid})
   when is_pid(TPid) ->
   NewState =
   case memory_queue:member(CorrelationId, Dedup) of
      true -> lager:notice("DUPLICATE from the takeover consumer with CorrId ~p, we should stop the consumer",
         [CorrelationId]),
         %% stop the takeover consumer
         %% unbind and delete queue
         TPid ! tookover,
         TPid ! stop,
         State#state{takeover_consumer_pid = undefined};
      false ->
         %% insert data in takeover data queue
         NewData = memory_queue:enq(CorrelationId, State#state.takeover_data),
%%         lager:notice("new takeover data is ~p",[memory_queue:to_list(NewData)]),
         State#state{takeover_data = NewData}
   end,

   {ok, NewState};
%% takeover node will get this message
handle_info(takeover_timeout, #state{consumer = Client, parent_pid = Parent} = State) ->
   lager:notice("time is up for takeover, will stop and delete queue"),
   Client ! unbind_delete_queue,
   Parent ! takeover_queue_done,
   {ok, State};
handle_info(tookover, #state{consumer = Client} = State) ->
   Client ! unbind_delete_queue,
   {ok, State};
handle_info(Other, #state{consumer = Client} = State) when is_pid(Client) ->
   lager:notice("AmqpConsumer Info:~p, client: ~p", [Other, Client]),
   {ok, State};
handle_info(_R, State) ->
   {ok, State}.

handle_ack(_, _, State=#state{flow_ack = false}) ->
   {ok, State};
handle_ack(Mode, DTag, State=#state{consumer = Consumer}) ->
   Func = case Mode of single -> ack; multi -> ack_multiple end,
   carrot:Func(Consumer, DTag),
   {ok, State}.

shutdown(#state{consumer = C, last_dtag = _DTag, emitter = Emitter}) ->
   connection_registry:disconnected(),
   catch gen_server:stop(C),
   catch (gen_server:stop(Emitter)).

enq_or_emit(Item, CorrId, DTag, State = #state{parent_pid = Parent}) when is_pid(Parent) ->
   %% inform parent consumer
   Parent ! {takeover_data, CorrId},
   %% emit now
   df_subscription:output(State#state.parent_subscriptions, Item, 1),
   {ok, maybe_ack(DTag, State)};
enq_or_emit(Item, _CorrId, DTag, State = #state{safe_mode = false}) ->
   {emit, Item, maybe_ack(DTag, State)};
enq_or_emit(Item, _CorrId, DTag, State = #state{esq_queue = Q}) ->
   ok = esq:enq(Item, Q),
   {ok, maybe_ack(DTag, State)}.

maybe_ack(_NewDTag, State = #state{confirm = false}) ->
   State;
maybe_ack(NewDTag, State = #state{collected = NumCollected}) ->
   maybe_ack(State#state{collected = NumCollected+1, last_dtag = NewDTag}).

maybe_ack(State = #state{flow_ack = true}) ->
   State;
maybe_ack(State = #state{last_dtag = undefined, collected = 0}) ->
   State;
maybe_ack(State = #state{collected = NumCollected, ack_every = NumCollected}) ->
   do_ack(State);
maybe_ack(State = #state{collected = 1}) ->
   restart_ack_timeout(State);
maybe_ack(State) ->
   State.

do_ack(State = #state{last_dtag = DTag, consumer = From, ack_timer = Timer, collected = _Num}) ->
   catch erlang:cancel_timer(Timer),
   carrot:ack_multiple(From, DTag),
   State#state{collected = 0, last_dtag = undefined, ack_timer = undefined}.

restart_ack_timeout(State = #state{ack_after = Time, ack_timer = Timer}) ->
   catch erlang:cancel_timer(Timer),
   NewTimer = erlang:send_after(Time, self(), ack_timeout),
   State#state{ack_timer = NewTimer}.
%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
%%% internal
%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%

build_item(Payload, RKey,
        S = #state{as = As, include_topic = AddTopic, topic_key = TopicKey, dt_field = DTField, dt_format = DTFormat,
           clean_names = Clean}) ->
   Msg0 = flowdata:from_json_struct(Payload, DTField, DTFormat, Clean),
   Item1 = check_item(Msg0, S),
   Item0 =
      case AddTopic of
         true -> flowdata:set_field(Item1, TopicKey, RKey);
         false -> Item1
      end,
   flowdata:set_root(Item0, As).

check_item(#data_batch{}, #state{flow_ack = true}) ->
   lager:warning("Cannot use flow_ack with data_batch items, " ++
      "make sure data_point items are consumed from amqp, e.g. use |unbatch() node before sending data to the broker!"),
   exit(stop_wrong_item_type);
%%   case Points of
%%      [ThePoint] -> ThePoint;
%%      _ -> exit(stop_wrong_item_type)
%%   end;
check_item(Item, _S) ->
   Item.

start_consumer(State = #state{opts = ConsumerOpts = #{queue := QName}}) ->
   connection_registry:connecting(),
   COpts = consumer_config(ConsumerOpts),
   case catch rmq_consumer:start_monitor(self(), COpts) of
      {ok, Pid, _NewConsumer} ->
         %% insert queue to registry
         queue_cleaner:add_q(State#state.flownodeid, {QName, maps:from_list(COpts)}),
         State#state{consumer = Pid};
      What -> lager:warning("Error when starting rmq consumer : ~p",[What]), State
   end.

start_emitter(State = #state{esq_queue = Q}) ->
   {ok, Emitter} = q_msg_forwarder:start_monitor(Q),
   State#state{emitter = Emitter}.


-spec consumer_config(Opts :: map()) -> list().
consumer_config(Opts = #{vhost := VHost, queue := Q, queue_type := QType, consumer_tag := ConsumerTag,
   prefetch := Prefetch, exchange := XChange, root_exchange := RootEx, bindings := Bindings,
   routing_key := RoutingKey, confirm := Confirm, passive := Passive}) ->

   QArgs = case QType of
              [] -> [];
              <<>> -> [];
              _ -> [{"x-queue-type", QType}]
           end,
   % Number of connections not relevant here,
   % because we start the consumer monitored not pooled
   Config0 =
      [
         {workers, 1},
         {callback, self()},
         {confirm, Confirm},
         {setup_type, permanent},
         {consumer_tag, ConsumerTag},
         {prefetch_count, Prefetch},
         {vhost, VHost}
      ],
   SetupQ =
         [{queue, [
            {queue, Q},
            {arguments, QArgs},
            {passive, Passive},
            {exchange, XChange},
            {routing_key, RoutingKey},
            {bindings, Bindings}
         ]}],
   SetupEx =
      case RootEx of
         undefined -> [];
         _ ->
            [{exchange, [
               {exchange, XChange},
               {type, <<"topic">>},
               {source, RootEx}
            ]}]
      end,
   Setup0 = SetupQ ++ SetupEx,
   Setup = [{setup, Setup0}],
   Config = Config0++Setup,
   Props = carrot_util:proplists_merge(
      maps:to_list(Opts) ++ [{ssl_opts, faxe_config:get_amqp_ssl_opts()}], Config),
   Props.


eval_name(#faxe_lambda{} = Lambda, _Opts, {_GraphId, _NodeId}) ->
   faxe_lambda:execute(#data_point{}, Lambda);
eval_name(undefined, _Opts, {GraphId, NodeId}) ->
   <<GraphId/binary, "_", NodeId/binary>>;
eval_name(Name, _Opts, _Idx) when is_binary(Name) ->
   Name.

maybe_takeover_timeout(S = #state{takeover_time = undefined}) ->
   S;
maybe_takeover_timeout(S = #state{takeover_time = Time, takeover_timer = Timer}) ->
   catch erlang:cancel_timer(Timer),
   NewTimer = erlang:send_after(Time, self(), takeover_timeout),
   S#state{takeover_timer = NewTimer}.

cancel_takeover_timeout(S = #state{takeover_time = undefined}) ->
   S;
cancel_takeover_timeout(S = #state{takeover_timer = Timer}) ->
   catch erlang:cancel_timer(Timer),
   S#state{takeover_timer = undefined}.