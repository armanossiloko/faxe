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

-define(META_FIELD, <<"_meta">>).

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
   %% map of seq check items in use, one per meta topic
   seq_checks = #{},
   seq_check_template :: #seq_check{},
   send_pool,
   remove_meta_field = true
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
   {ssl, is_set, {mqtt, ssl, enable}},
   {remove_meta_field, boolean, {seq_check, cleanup}}
].

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
      ssl := UseSSL, qos := Qos, client_id := CId, remove_meta_field := RemoveMeta} = _Opts) ->

   Host = binary_to_list(Host0),
   process_flag(trap_exit, true),
   ClientId = case CId of undefined -> <<GId/binary, "_", NId/binary>>; _ -> CId end,

   reconnect_watcher:new(10000, 5, io_lib:format("~s:~p ~p",[Host, Port, ?MODULE])),
   Reconnector = faxe_backoff:new({100, 4200}),
   {ok, Reconnector1} = faxe_backoff:execute(Reconnector, connect),

   connection_registry:reg(NodeId, Host, Port, <<"mqtt">>),
   State = #state{host = Host, port = Port, topic = Topic, dt_field = DTField, dt_format = DTFormat,
      ssl = UseSSL, qos = Qos, client_id = ClientId, remove_meta_field = RemoveMeta,
      topics = Topics, include_topic = IncludeTopic, topic_key = TopicKey, as = As,
      reconnector = Reconnector1, user = User, pass = Pass, fn_id = NodeId, ssl_opts = ssl_opts(UseSSL)},
   MqttOpts = build_mqtt_opts(State),
   %% mqtt publish is needed, when we do the sequence check
   Pool = mqtt_pub_pool_manager:connect(maps:from_list(MqttOpts)),
   SeqCheckTemplate = seq_check_new(),
   {ok, State#state{mqtt_opts = MqttOpts, seq_check_template = SeqCheckTemplate, send_pool = Pool}}.

ssl_opts(false) ->
   [];
ssl_opts(true) ->
   faxe_config:get_mqtt_ssl_opts().

process(_In, _, State = #state{}) ->
   {ok, State}.


handle_info(connect, State) ->
   {ok, connect(State)};
handle_info({mqttc, _C, connected}, State=#state{reconnector = Recon}) ->
   connection_registry:connected(),
%%   lager:info("~p mqtt client connected to ~p",[?MODULE, Host]),
   NewState = State#state{connected = true, reconnector = faxe_backoff:reset(Recon)},
   subscribe(NewState),
   {ok, NewState};
handle_info({mqttc, _C,  disconnected}, State=#state{client = _Client}) ->
   connection_registry:disconnected(),
%%   lager:debug("~p mqtt client disconnected!!",[?MODULE]),
   {ok, State#state{connected = false }};
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
handle_info({mqtt_connected, _Host}, State) ->
   {ok, State};
handle_info(Msg, State) ->
   lager:notice("got message ~p",[Msg]),
   {ok, State}.

shutdown(#state{client = C}) ->
   catch emqtt:disconnect(C),
   catch emqtt:stop(C).

data_received(Topic, Payload,
    S = #state{dt_field = DTField, dt_format = DTFormat, remove_meta_field = RemoveMeta,
       include_topic = AddTopic, topic_key = TopicKey, as = As}) ->
   node_metrics:metric(?METRIC_BYTES_READ, byte_size(Payload), S#state.fn_id),
   node_metrics:metric(?METRIC_ITEMS_IN, 1, S#state.fn_id),
   Item0 = flowdata:from_json_struct(Payload, DTField, DTFormat),
   {_T, StateNew} = timer:tc(fun check_seq/3, [Item0, Topic, S]),
%%   case T > 100 of true -> lager:warning("time for check_seq: ~pmy",[T]); _ -> ok end,
   dataflow:maybe_debug(item_in, 1, Item0, StateNew#state.fn_id, StateNew#state.debug_mode),
   Item1 =
   case AddTopic of
      true -> flowdata:set_field(Item0, TopicKey, Topic);
      false -> Item0
   end,
   Item2 =
   case RemoveMeta of
      true -> flowdata:delete_field(Item1, ?META_FIELD);
      false -> Item1
   end,
   Item = flowdata:set_root(Item2, As),
   {emit, {1, Item}, StateNew}.

check_seq(
    Item = #data_point{fields = #{?META_FIELD := #{<<"topic">> := Topic, <<"seq">> := Seq} = Meta }},
    FullTopic,
    State = #state{seq_checks = SeqChecks, send_pool = PoolKey}) ->

%%   faxe_seq_check_manager:handle(Topic, Item),

   SeqCheck0 = get_check(FullTopic, Topic, State, Item),
   SeqCheck = SeqCheck0#seq_check{last_meta = Meta},
   MaxSeqBuffer = SeqCheck#seq_check.max_buffer_size,
   List = SeqCheck#seq_check.seq_buffer,
   NewList = [Seq|List],
   {NewList1, NewSeqCheck} =
   case length(NewList) >= MaxSeqBuffer of
      true ->
         {EvalResult, EvalRest, NSeqCheck} = eval_seq_list(NewList, SeqCheck, PoolKey),
         {EvalResult ++ EvalRest, NSeqCheck};
      false ->
         {NewList, SeqCheck}
   end,
   NewSeqCheck1 = NewSeqCheck#seq_check{seq_buffer = NewList1},
   State#state{seq_checks = SeqChecks#{FullTopic => NewSeqCheck1}};
check_seq(_Item, _Topic, State) ->
   State.

get_check(_FullTopic, Topic, #state{seq_check_template = Template},
    #data_point{fields = #{?META_FIELD := #{<<"started">> := true}}}) ->
   seq_check_inst(Topic, Template);
get_check(FullTopic, _Topic, #state{seq_checks = SeqChecks}, _Item) when is_map_key(FullTopic, SeqChecks) ->
   maps:get(FullTopic, SeqChecks);
get_check(_FullTopic, Topic, #state{seq_check_template = Template}, _Item) ->
   seq_check_inst(Topic, Template).


eval_seq_list(List, SeqCheck =
      #seq_check{max_buffer_size = MaxSeqBuff, last_seq = LastSeq, seq_threshold = Threshold}, PoolKey) ->

   EvalLen = erlang:round(MaxSeqBuff/5),
   %% get the ordered list of all
   SeqListAll = ordsets:to_list(ordsets:from_list(List)),
   %% split the list and at the same time, get the keys from the left list
   MinSeq =
   case LastSeq of
      undefined -> undefined;
      Other when Other >= Threshold -> 0;
      _ -> LastSeq
   end,
   {First0, KeyList, RList} =
   case catch split_get_keys(EvalLen, SeqListAll, MinSeq) of
      {[First01|_] = KeyList1, RList1} -> {First01, KeyList1, RList1};
      What -> lager:warning("called split_get_keys with ~p, ~w ~p(~p) got ~w",[EvalLen, SeqListAll, MinSeq, Threshold, What]),
         {0, [], []}
   end,
   case length(KeyList) < EvalLen of
      true -> lager:warning("keylist is shorter than evalLen (~p) with ~w minseq: ~p",[EvalLen, SeqListAll, MinSeq]);
      false -> ok
   end,
   First = case MinSeq of undefined -> First0; _ -> case MinSeq+1 >= Threshold of true -> 1; false -> MinSeq+1 end end,
   Last0 = First + length(KeyList) - 1,
   {Last, LastSeq1} =
   case Last0 >= Threshold of
      true -> {Threshold, 0};
      false -> {Last0, Last0}
   end,
   %% build a check list to get the difference
   CheckList = lists:seq(First, Last),
   %% get the missing keys
   MissingList = CheckList -- KeyList,
   %% get the remaining keys in the sequence list
   RemainingList = KeyList -- CheckList,
%%   lager:notice("~nminkey: ~p ||| check ~w |||| seqlist: ~w |||| missing: ~w, remaining: ~w,  first: ~w, last: ~w, last_seq ~w",
%%      [MinSeq, CheckList, KeyList, MissingList, RemainingList, First, Last, LastSeq1]),
   spawn(fun() -> report_seq(MissingList, SeqCheck, PoolKey) end),
   {RemainingList, RList, SeqCheck#seq_check{last_seq = LastSeq1}}.


-spec split_get_keys(N :: pos_integer(), L::list(), Min::undefined|list()) -> {list(), list(), list()}.
split_get_keys(N, L, Min) ->
%%   lager:notice("split_get_keys(~p, ~p, ~p)",[N, lists:reverse(L), Min]),
   split_get_keys(N, L, {[], []}, Min).

-spec split_get_keys(non_neg_integer(), L::list(), tuple(), _Min::undefined|list()) -> tuple().
split_get_keys(0, L, {K, Skipped}, _Min) ->
   {lists:reverse(K, []), L++Skipped};
split_get_keys(_, [], {K, Skipped}, _Min) ->
   {lists:reverse(K, []), Skipped};
split_get_keys(N, [HK|T], {K, Skipped}, undefined) ->
   split_get_keys(N-1, T, {[HK|K], Skipped}, undefined);
split_get_keys(N, [HK|T], {K, Skipped}, Min) when HK > Min ->
   split_get_keys(N-1, T, {[HK|K], Skipped}, Min);
split_get_keys(N, [H|T], {K, Skipped}, Min) ->
   split_get_keys(N, T, {K, [H|Skipped]}, Min).


report_seq(MissingList, SeqCheck, PoolKey) ->
   Reports = build_check_report(MissingList, SeqCheck),
   send_reports(Reports, PoolKey).

build_check_report([], #seq_check{report_topic_mask = _Topic}) ->
   [];
build_check_report(MissingList, #seq_check{report_topic = SendTopic, last_meta = Meta}) ->
   F =
      fun(SeqKey) ->
         DP = flowdata:new(),
         Fields = Meta#{
%%            <<"seq_rel">> => RelKey,
            <<"seq">> => SeqKey},
         {SendTopic, DP#data_point{fields = Fields}}
         end,
   Reports = lists:map(F, MissingList),
   [lager:notice("send report ~p",[P]) || P <- Reports],
   Reports.

build_report_topic(SourceTopic, #seq_check{report_topic_mask = TopicTemplate, meta_topic_mapping = Mask}) ->
   Parts = string:lexemes(SourceTopic, "/"),
   maps:fold(
      fun(Field, Index, TempTopic) ->
         Replacement =
         case catch lists:nth(Index, Parts) of
            R when is_binary(R) -> R;
            _ -> Field
         end,
         binary:replace(TempTopic, Field, Replacement)
      end, TopicTemplate, Mask).


send_reports([], _Key) ->
   ok;
send_reports(ReportList, PoolKey) ->
   {ok, Publisher} = mqtt_pub_pool_manager:get_connection(PoolKey),
   F = fun({Topic, Item}) ->
      Json = flowdata:to_json(Item),
      Publisher ! {publish, {Topic, Json, 1, false}}
       end,
   lists:foreach(F, ReportList).

-spec seq_check_new() -> #seq_check{}.
seq_check_new() ->
   SeqCheck = #seq_check{},
   SeqCheckConfig = faxe_config:get(seq_check),
   WinSize = proplists:get_value(win_size, SeqCheckConfig, SeqCheck#seq_check.max_buffer_size),
   Mask = proplists:get_value(topic_mask, SeqCheckConfig, SeqCheck#seq_check.report_topic_mask),
   Mapping0 = faxe_util:to_bin(proplists:get_value(topic_mapping, SeqCheckConfig)),
   Threshold = proplists:get_value(max_seq_num, SeqCheckConfig),


   Mapping =
   case catch jiffy:decode(Mapping0, [return_maps]) of
      M when is_map(M) -> M;
      _ -> SeqCheck#seq_check.meta_topic_mapping
   end,
   #seq_check{
      report_topic_mask = faxe_util:to_bin(Mask), max_buffer_size = WinSize,
      meta_topic_mapping = Mapping, seq_threshold = Threshold}.

seq_check_inst(Topic, SeqCheck) ->
   ReportTopic = build_report_topic(Topic, SeqCheck),
   SeqCheck#seq_check{meta_topic = Topic, report_topic = ReportTopic}.
%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%


connect(State = #state{mqtt_opts = Opts, client_id = ClientId}) ->
   connection_registry:connecting(),
   reconnect_watcher:bump(),
   S = self(),
   MsgHandler = #{
      publish => fun(Pack) -> S ! {publish, Pack} end,
      connected => fun(Props) -> S ! {mqttc, Props, connected} end,
      disconnected => fun(Reason) -> S ! {mqttc, Reason, disconnected} end
   },
   Opts0 = [
      {msg_handler, MsgHandler},
      {clientid, ClientId}
   ],
   Opts1 = Opts ++ Opts0,

   {ok, Client} = emqtt:start_link(Opts1),
%%   {ok, _Props} =
      emqtt:connect(Client),
%%   lager:notice("connect to mqtt broker gives: ~p",[Client]),
   State#state{client = Client}.

build_mqtt_opts(State = #state{host = Host, port = Port}) ->
   Opts0 = [
      {host, Host},
      {port, Port},
      {reconnect, infinity}, {reconnect_timeout, 100},
      {owner, self()},
      {keepalive, 15}, {connect_timeout, 20000},
      {clean_start, false}
   ],
   Opts1 = opts_auth(State, Opts0),
   opts_ssl(State, Opts1).

opts_auth(#state{user = <<>>}, Opts) -> Opts;
opts_auth(#state{user = undefined}, Opts) -> Opts;
opts_auth(#state{user = User, pass = Pass}, Opts) ->
   [{username, User},{password, Pass}] ++ Opts.
opts_ssl(#state{ssl = false}, Opts) -> Opts;
opts_ssl(#state{ssl = true, ssl_opts = SslOpts}, Opts) ->
   [{ssl, true}, {ssl_opts, SslOpts}]++ Opts.


subscribe(#state{qos = Qos, client = C, topic = Topic, topics = undefined}) when is_binary(Topic) ->
   emqtt:subscribe(C, Topic, Qos);
subscribe(#state{qos = Qos, client = C, topics = Topics}) ->
   TQs = [{Top, Qos} || Top <- Topics],
   emqtt:subscribe(C, TQs).

