%%%-------------------------------------------------------------------
%%% @author heyoka
%%% @copyright (C) 2025, <COMPANY>
%%% @doc
%%% @end
%%%-------------------------------------------------------------------
-module(faxe_seq_check_manager).

-behaviour(gen_server).

-include("faxe.hrl").

-export([start_link/0]).
-export([init/1, handle_call/3, handle_cast/2, handle_info/2, terminate/2,
  code_change/3]).
-export([handle/2]).

-define(SERVER, ?MODULE).

-define(TABLE_TOPIC, faxe_seq_checks_topic).
-define(TABLE_PID, faxe_seq_checks_pid).
-define(META_FIELD, <<"_meta">>).

-record(state, {
  checks :: [pid()],
  template :: #seq_check{}
}).

%%%==================================================================
%%% public api
%%%==================================================================
%%handle(Topic, #data_point{fields = #{?META_FIELD := #{<<"started">> := true}}}) ->
%%  CheckServer = get_check(Topic),
%%  reset_check_server(Topic),

handle(Topic, Item) ->
  Check = get_check(Topic),
  Check ! {handle, Topic, Item}.

get_check(Topic) ->
  %% get seq check from ets table
  case ets:lookup(?TABLE_TOPIC, Topic) of
    [] -> start_check_server(Topic);
    [{Topic, SeqCheckServerPid}]  -> SeqCheckServerPid
  end.

start_check_server(Topic) ->
  {ok, Pid} = gen_server:call(?SERVER, {start_check, Topic}),
  Pid.


%%%===================================================================
%%% Spawning and gen_server implementation
%%%===================================================================

start_link() ->
  gen_server:start_link({local, ?SERVER}, ?MODULE, [], []).

init([]) ->
  {ok, #state{template = get_template()}}.

handle_call({start_check, Topic}, _From, State = #state{template = Template}) ->
  Check = seq_check_inst(Topic, Template),
  {ok, Pid} = faxe_seq_check:start_link(Check),
  ets:insert(?TABLE_TOPIC, {Topic, Pid}),
  ets:insert(?TABLE_PID, {Pid, Topic}),
  erlang:monitor(process, Pid),
  {reply, {ok, Pid}, State}.

handle_cast(_Request, State = #state{}) ->
  {noreply, State}.

handle_info({'DOWN', _Mon, process, Pid, Info}, State = #state{checks = _ChecksList}) ->
  lager:notice("seq_check is down with Info: ~p",[Info]),
  case ets:lookup(?TABLE_PID, Pid) of
    [] -> ok;
    [{Pid, Topic}]  ->
      ets:delete(?TABLE_PID, Pid),
      ets:delete(?TABLE_TOPIC, Topic)
  end,
  {noreply, State};
handle_info(Info, State = #state{}) ->
  lager:info("unexpected info ~p",[Info]),
  {noreply, State}.

terminate(_Reason, _State = #state{}) ->
  ok.

code_change(_OldVsn, State = #state{}, _Extra) ->
  {ok, State}.

%%%===================================================================
%%% Internal functions
%%%===================================================================
-spec get_template() -> #seq_check{}.
get_template() ->
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