%%%-------------------------------------------------------------------
%%% @author heyoka
%%% @copyright (C) 2025, <COMPANY>
%%% @doc
%%% @end
%%%-------------------------------------------------------------------
-module(faxe_seq_check_manager).

-behaviour(gen_server).

-include("faxe.hrl").

-export([start_link/0, count/3]).
-export([init/1, handle_call/3, handle_cast/2, handle_info/2, terminate/2, code_change/3]).
-export([handle/1]).

-define(SERVER, ?MODULE).

-define(TABLE_KEY, faxe_seq_checks_key).
-define(TABLE_PID, faxe_seq_checks_pid).
-define(TABLE_COUNT, faxe_seq_checks_count_missing).


-define(META_FIELD, <<"_meta">>).
-define(META_KEY_DEVICE, <<"device">>).
-define(META_KEY_TOPIC, <<"topic">>).

-record(state, {
  template :: #seq_check{},
  mqtt_pool
}).

%%%==================================================================
%%% public api
%%%==================================================================
%%handle(Topic, #data_point{fields = #{?META_FIELD := #{<<"started">> := true}}}) ->
%%  CheckServer = get_check(Topic),
%%  reset_check_server(Topic),

handle(Item = #data_point{fields = #{?META_FIELD := #{?META_KEY_DEVICE := Device, ?META_KEY_TOPIC := Topic}}}) ->
  Check = get_check({Device, Topic}),
  Check ! {handle, Item};
handle(_) ->
  ok.

get_check(Key) ->
  %% get seq check from ets table
  case ets:lookup(?TABLE_KEY, Key) of
    [] -> start_check_server(Key);
    [{Key, SeqCheckServerPid}]  -> SeqCheckServerPid
  end.

start_check_server(Key) ->
  {ok, Pid} = gen_server:call(?SERVER, {start_check, Key}),
  Pid.

count(Key, CheckedInc, MissingInc) ->
  ets:update_counter(?TABLE_COUNT, Key, [{2, CheckedInc}, {3, MissingInc}], {Key, 0, 0}).

%%%===================================================================
%%% Spawning and gen_server implementation
%%%===================================================================

start_link() ->
  gen_server:start_link({local, ?SERVER}, ?MODULE, [], []).

init([]) ->
  MqttPool = mqtt_pub_pool_manager:connect(),
  Template = get_template(MqttPool),
  {ok, #state{template = Template, mqtt_pool = MqttPool}}.

handle_call({start_check, {_Device, Topic}=Key}, _From, State = #state{template = Template}) ->
  Check = seq_check_inst(Topic, Key, Template),
%%  lager:notice("seq check instance: ~p",[lager:pr(Check, ?MODULE)]),
  {ok, Pid} = faxe_seq_check:start_link(Check),
  ets:insert(?TABLE_KEY, {Key, Pid}),
  ets:insert(?TABLE_PID, {Pid, Key}),
  erlang:monitor(process, Pid),
  {reply, {ok, Pid}, State}.

handle_cast(_Request, State = #state{}) ->
  {noreply, State}.

handle_info({'DOWN', _Mon, process, Pid, Info}, State = #state{}) ->
  lager:notice("seq_check is down with Info: ~p",[Info]),
  case ets:lookup(?TABLE_PID, Pid) of
    [] -> ok;
    [{Pid, Key}]  ->
      ets:delete(?TABLE_PID, Pid),
      ets:delete(?TABLE_KEY, Key)
  end,
  {noreply, State};
handle_info(_Info, State = #state{}) ->
  {noreply, State}.

terminate(_Reason, _State = #state{}) ->
  ok.

code_change(_OldVsn, State = #state{}, _Extra) ->
  {ok, State}.

%%%===================================================================
%%% Internal functions
%%%===================================================================
-spec get_template(term()) -> #seq_check{}.
get_template(PoolKey) ->
  SeqCheck = #seq_check{},
  SeqCheckConfig = faxe_config:get(seq_check),
  WinSize = proplists:get_value(win_size, SeqCheckConfig, SeqCheck#seq_check.max_buffer_size),
  MinEvalSize = proplists:get_value(min_eval_size, SeqCheckConfig, SeqCheck#seq_check.min_eval_size),
  EvalTimeout = proplists:get_value(eval_timeout, SeqCheckConfig, SeqCheck#seq_check.eval_timeout),
  MaxAge = proplists:get_value(max_age, SeqCheckConfig, SeqCheck#seq_check.max_age),
  Mask = proplists:get_value(topic_mask, SeqCheckConfig, SeqCheck#seq_check.report_topic_mask),
  MaskLate = proplists:get_value(topic_mask_late, SeqCheckConfig, SeqCheck#seq_check.report_topic_mask_late),
  Mapping0 = faxe_util:to_bin(proplists:get_value(topic_mapping, SeqCheckConfig)),
  Threshold = proplists:get_value(max_seq_num, SeqCheckConfig),
  EvalSize = erlang:round(WinSize/5),

  Mapping =
    case catch jiffy:decode(Mapping0, [return_maps]) of
      M when is_map(M) -> M;
      _ -> SeqCheck#seq_check.meta_topic_mapping
    end,
  #seq_check{
    report_topic_mask = faxe_util:to_bin(Mask),
    report_topic_mask_late = faxe_util:to_bin(MaskLate),
    max_buffer_size = WinSize, min_eval_size = MinEvalSize, pool_key = PoolKey,
    meta_topic_mapping = Mapping, seq_threshold = Threshold, eval_size = EvalSize,
    eval_timeout = EvalTimeout, max_age = MaxAge}.

seq_check_inst(Topic, Key, SeqCheck=#seq_check{report_topic_mask = TopicMask, report_topic_mask_late = TopicMaskLate}) ->
  ReportTopic = build_report_topic(Topic, TopicMask, SeqCheck),
  ReportTopicLate = build_report_topic(Topic, TopicMaskLate, SeqCheck),
  SeqCheck#seq_check{report_topic = ReportTopic, report_topic_late = ReportTopicLate, key = Key}.

build_report_topic(SourceTopic, TopicTemplate, #seq_check{meta_topic_mapping = Mask}) ->
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