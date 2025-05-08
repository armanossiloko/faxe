%%%-------------------------------------------------------------------
%%% @author heyoka
%%% @copyright (C) 2019, <COMPANY>
%%% @doc
%%% Receive data from an mqtt-broker.
%%% @end
%%% Created : 27. May 2019 09:00
%%%-------------------------------------------------------------------
-module(esp_mqtt_subscribe).
-author("heyoka").

%% API
-behavior(df_component).

-include("faxe.hrl").
%% API
-export([init/3, process/3, options/0, handle_info/2, shutdown/1, check_options/0, metrics/0]).

-define(DEFAULT_PORT, 1883).
-define(DEFAULT_SSL_PORT, 8883).

-record(seq_check, {
   seq_buffer = [],
   max_buffer_size = 30,
   last_eval_buffer = [],
   report_topic = <<"tgw/data/{site}/Mqtt_Metric/{dataformat}">>,
   meta_topic_mask = #{3 => <<"{site}">>, 4 => <<"{dataformat}">>, 5 => <<"blupp">>}
}).

%% state for direct publish mode
-record(state, {
   client,
   connected = false,
   reconnector,
   host,
   port,
   user,
   pass,
   qos,
   topic,
   topics,
   client_id,
   dt_field,
   dt_format,
   ssl = false,
   ssl_opts = [],
   topics_seen = [],
   fn_id,
   include_topic = true,
   topic_key,
   as,
   debug_mode = false,
   mqtt_opts = [],

   seq_checks = #{},
   last_seq = #{},
   tree_seq = #{},
   seq_threshold = 900
}).


options() -> [
   {host, binary, {mqtt, host}},
   {port, integer, {mqtt, port}},
   {user, string, {mqtt, user}},
   {pass, string, {mqtt, pass}},
   {client_id, string, undefined},
   {qos, integer, 1},
   {topic, binary, undefined},
   {topics, binary_list, undefined},
   {dt_field, string, <<"ts">>},
   {dt_format, string, ?TF_TS_MILLI},
   {include_topic, bool, true},
   {topic_as, string, <<"topic">>},
   {as, string, undefined},
   {ssl, is_set, {mqtt, ssl, enable}}].

check_options() ->
   [
      {one_of_params, [topic, topics]},
      {func, topic,
         fun
            (undefined) -> true;
            (T) -> faxe_util:check_mqtt_topic(T)
         end,
         <<": ">>},
      {func, topics, fun check_topics/1, <<" at least one of the topics seems to be invalid">>}
   ].

check_topics(undefined) -> true;
check_topics(T) when is_binary(T) ->
   faxe_util:check_mqtt_topic(T) == true;
check_topics(Ts) when is_list(Ts) ->
   lists:all(fun(E) -> check_topics(E) end, Ts).

metrics() ->
   [
%%      {?METRIC_SENDING_TIME, histogram, [slide, 60], "Network time for sending a message."},
      {?METRIC_BYTES_READ, meter, [], "Size of item sent in kib."}
   ].

init({GId, NId}=NodeId, _Ins,
   #{ host := Host0, port := Port, topic := Topic, topics := Topics, dt_field := DTField, as := As,
      dt_format := DTFormat, user := User, pass := Pass, include_topic := IncludeTopic, topic_as := TopicKey,
      ssl := UseSSL, qos := Qos, client_id := CId} = _Opts) ->

   Host = binary_to_list(Host0),

   process_flag(trap_exit, true),
   ClientId = case CId of undefined -> <<GId/binary, "_", NId/binary>>; _ -> CId end,

   reconnect_watcher:new(10000, 5, io_lib:format("~s:~p ~p",[Host, Port, ?MODULE])),
   Reconnector = faxe_backoff:new({100, 4200}),
   {ok, Reconnector1} = faxe_backoff:execute(Reconnector, connect),

   connection_registry:reg(NodeId, Host, Port, <<"mqtt">>),
   State = #state{host = Host, port = Port, topic = Topic, dt_field = DTField, dt_format = DTFormat,
      ssl = UseSSL, qos = Qos, client_id = ClientId,
      topics = Topics, include_topic = IncludeTopic, topic_key = TopicKey, as = As,
      reconnector = Reconnector1, user = User, pass = Pass, fn_id = NodeId, ssl_opts = ssl_opts(UseSSL)},
   MqttOpts = build_mqtt_opts(State),
   %% mqtt publish is needed, when we do the sequence check
   mqtt_pub_pool_manager:connect(maps:from_list(MqttOpts)),
   {ok, State#state{mqtt_opts = MqttOpts}}.

ssl_opts(false) ->
   [];
ssl_opts(true) ->
   faxe_config:get_mqtt_ssl_opts().

process(_In, _, State = #state{}) ->
   {ok, State}.


handle_info(connect, State) ->
   connect(State),
   {ok, State};
handle_info({mqttc, C, connected}, State=#state{host = Host, reconnector = Recon}) ->
   connection_registry:connected(),
   lager:debug("mqtt client connected to ~p",[Host]),
   NewState = State#state{client = C, connected = true, reconnector = faxe_backoff:reset(Recon)},
   subscribe(NewState),
   {ok, NewState};
%% @todo do we have to kill the client ?
handle_info({mqttc, _C,  disconnected}, State=#state{client = Client}) ->
   catch exit(Client, kill),
   connection_registry:disconnected(),
   lager:debug("mqtt client disconnected!!"),
   {ok, State#state{connected = false, client = undefined}};
%% for emqtt
handle_info({publish, #{payload := Payload, topic := Topic} }, S=#state{}) ->
   data_received(Topic, Payload, S);
%% for emqttc
handle_info({publish, Topic, Payload }, S=#state{}) ->
   data_received(Topic, Payload, S);
handle_info({disconnected, shutdown, tcp_closed}=M, State = #state{}) ->
   lager:info("emqtt : ~p", [M]),
   {ok, State};
handle_info({'EXIT', _C, _Reason}, State = #state{reconnector = Recon, host = H, port = P}) ->
   connection_registry:disconnected(),
   lager:notice("EXIT emqtt: ~p [~p]", [_Reason,{H, P}]),
   {ok, Reconnector} = faxe_backoff:execute(Recon, connect),
   {ok, State#state{connected = false, client = undefined, reconnector = Reconnector}};
handle_info(start_debug, State) -> {ok, State#state{debug_mode = true}};
handle_info(stop_debug, State) -> {ok, State#state{debug_mode = false}};
handle_info(_What, State) ->
   {ok, State}.

shutdown(#state{client = C}) ->
   catch (emqttc:disconnect(C)).

data_received(Topic, Payload,
    S = #state{dt_field = DTField, dt_format = DTFormat, include_topic = AddTopic, topic_key = TopicKey, as = As}) ->
   node_metrics:metric(?METRIC_BYTES_READ, byte_size(Payload), S#state.fn_id),
   node_metrics:metric(?METRIC_ITEMS_IN, 1, S#state.fn_id),
   Item0 = flowdata:from_json_struct(Payload, DTField, DTFormat),
   State = check_seq(Item0, S),
   dataflow:maybe_debug(item_in, 1, Item0, State#state.fn_id, State#state.debug_mode),
   Item1 =
   case AddTopic of
      true -> flowdata:set_field(Item0, TopicKey, Topic);
      false -> Item0
   end,
   Item = flowdata:set_root(Item1, As),
   {emit, {1, Item}, State}.

check_seq(Item = #data_point{fields = #{<<"_meta">> := #{<<"topic">> := Topic, <<"seq">> := Seq} = Meta }},
    State = #state{seq_checks = SeqChecks}) ->

   SeqCheck = case maps:get(Topic, SeqChecks, nil) of nil -> #seq_check{}; T -> T end,
   MaxSeqBuffer = SeqCheck#seq_check.max_buffer_size,
   List = SeqCheck#seq_check.seq_buffer,
   NewList = [{Seq, Meta}|List],

   {NewList1, NewSeqCheck} =
   case length(NewList) >= MaxSeqBuffer of
      true ->
         {EvalResult, EvalRest, NSeqCheck, Reports} = eval_seq_list(NewList, SeqCheck),
         send_reports(Reports, State#state.host),
         {[{K, proplists:get_value(K, NewList)} || K <- EvalResult] ++ EvalRest, NSeqCheck};

      false -> {NewList, SeqCheck}
   end,
   NewSeqCheck1 = NewSeqCheck#seq_check{seq_buffer = NewList1},
   State#state{seq_checks = SeqChecks#{Topic => NewSeqCheck1}};
check_seq(_Item, State) ->
   State.

send_reports([], _Host) ->
   ok;
send_reports(ReportList, Host) ->
   {ok, Publisher} = mqtt_pub_pool_manager:get_connection(Host),
   F = fun({Topic, Item}) ->
      Json = flowdata:to_json(Item),
      Publisher ! {publish, {Topic, Json, 1, false}}
      end,
   lists:foreach(F, ReportList).


eval_seq_list(List, SeqCheck = #seq_check{max_buffer_size = MaxSeqBuff, last_eval_buffer = Seen}) ->
   EvalLen = erlang:round(MaxSeqBuff/3),
%%   lager:notice("eval list ~p",[orddict:to_list(orddict:from_list(List))]),
   %% get the ordered list of all
   SeqListAll = orddict:to_list(orddict:from_list(List)),
   %% split the list (in half)
   {SeqList, RList} = lists:split(EvalLen, SeqListAll),
   %% get all keys from the ordered splitted list to work with
   [First|_] = KeyList = lists:sort(proplists:get_keys(SeqList)),
   Last = First + length(KeyList) - 1,
   %% build a check list to get the difference
   CheckList = lists:seq(First, Last),
   %% get the missing keys
   MissingList = CheckList -- (KeyList ++ Seen),
   %% get the remaining keys in the sequence list
   RemainingList = KeyList -- CheckList,
%%   lager:notice("all: ~w ~nrest: ~w~ncheck ~w |||| seqlist: ~w |||| result: ~w, remaining: ~w,  first: ~w, last: ~w",
   lager:notice("~ncheck ~w |||| seqlist: ~w |||| lastseen: ~w |||| missing: ~w, remaining: ~w,  first: ~w, last: ~w",
      [CheckList, KeyList, Seen, MissingList, RemainingList, First, Last]),
%%      [SeqList, RList, CheckList, KeyList, MissingList, RemainingList, First, Last]),
   Reports = build_check_report(MissingList, SeqList, SeqCheck),
   NewEvalBuffer = lists:sublist(KeyList++Seen, trunc(MaxSeqBuff/2)),
   {RemainingList, RList, SeqCheck#seq_check{last_eval_buffer = NewEvalBuffer}, Reports}.

build_check_report([], _SeqList, #seq_check{report_topic = _Topic}) ->
   [];
build_check_report(MissingList, SeqList, SeqCheck = #seq_check{}) ->
   SeqTree = gb_trees:from_orddict(orddict:from_list(SeqList)),
   F =
      fun(SeqKey) ->
         DP = flowdata:new(),
         {K0, Meta0=#{<<"topic">> := MTopic}} = gb_trees:smaller(SeqKey, SeqTree),
         Fields = Meta0#{<<"seq_prev">> => K0, <<"seq">> => SeqKey},
         SendTopic = build_report_topic(MTopic, SeqCheck),
         {SendTopic, DP#data_point{fields = Fields}}
         end,
   Reports = lists:map(F, MissingList),
   [lager:notice("send report ~p",[P]) || P <- Reports],
   Reports.

build_report_topic(SourceTopic, #seq_check{report_topic = TopicTemplate, meta_topic_mask = Mask}) ->
   Parts = string:lexemes(SourceTopic, "/"),
   maps:fold(
      fun(Index, Field, TempTopic) ->
         Replacement =
         case catch lists:nth(Index, Parts) of
            R when is_binary(R) -> R;
            _ -> <<"not_found">>
         end,
         binary:replace(TempTopic, Field, Replacement)
      end, TopicTemplate, Mask).



connect(#state{mqtt_opts = Opts}) ->
   connection_registry:connecting(),
   reconnect_watcher:bump(),
   {ok, _Client} = emqttc:start_link(Opts++[{reconnect, 3, 120, 10}])
.

build_mqtt_opts(State = #state{host = Host, port = Port, client_id = ClientId}) ->
   Opts0 = [
      {host, Host},
      {port, Port},
      {keepalive, 15},
      {client_id, ClientId},
      {clean_sess, false}
   ],
   Opts1 = opts_auth(State, Opts0),
   opts_ssl(State, Opts1).

opts_auth(#state{user = <<>>}, Opts) -> Opts;
opts_auth(#state{user = undefined}, Opts) -> Opts;
opts_auth(#state{user = User, pass = Pass}, Opts) ->
   [{username, User},{password, Pass}] ++ Opts.
opts_ssl(#state{ssl = false}, Opts) -> Opts;
opts_ssl(#state{ssl = true, ssl_opts = SslOpts}, Opts) ->
   [{ssl, SslOpts}] ++ Opts.


subscribe(#state{qos = Qos, client = C, topic = Topic, topics = undefined}) when is_binary(Topic) ->
   ok = emqttc:subscribe(C, Topic, Qos);
subscribe(#state{qos = Qos, client = C, topics = Topics}) ->
   TQs = [{Top, Qos} || Top <- Topics],
   ok = emqttc:subscribe(C, TQs).

