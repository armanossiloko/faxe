%%%-------------------------------------------------------------------
%%% @author heyoka
%%% @copyright (C) 2025, <COMPANY>
%%% @doc
%%% @end
%%%-------------------------------------------------------------------
-module(faxe_seq_check_old).

-behaviour(gen_server).

-include("faxe.hrl").

-export([start_link/1]).
-export([init/1, handle_call/3, handle_cast/2, handle_info/2, terminate/2,
  code_change/3]).

-define(TABLE, faxe_seq_checks).

-define(META_FIELD, <<"_meta">>).
-define(SEQ_FIELD, <<"seq">>).

%%%==================================================================
%%% public api
%%%==================================================================

%%%===================================================================
%%% Spawning and gen_server implementation
%%%===================================================================

start_link(CheckState) ->
  gen_server:start_link(?MODULE, [CheckState], []).

init([CheckState0]) ->
  CheckState = start_eval_timer(CheckState0),
  {ok, CheckState}.

handle_call(_Request, _From, State = #seq_check{}) ->
  {reply, ok, State}.

handle_cast(_Request, State = #seq_check{}) ->
  {noreply, State}.

handle_info({handle, Item = #data_point{fields = #{?META_FIELD := #{<<"started">> := true}}}}, State = #seq_check{}) ->
  NewState = State#seq_check{last_meta = #{}, last_seq = undefined, seq_buffer = []},
  check_seq(Item, NewState);
handle_info({handle, Item}, State = #seq_check{}) ->
  check_seq(Item, State);
handle_info(check, S = #seq_check{seq_buffer = Buffer, min_eval_size = MinEval}) when length(Buffer) >= MinEval ->
%%  lager:notice("check timeout!"),
  {noreply, do_check(S)};
handle_info(check, State = #seq_check{}) ->
  {noreply, start_eval_timer(State)};
handle_info(_Info, State = #seq_check{}) ->
  {noreply, State}.

terminate(_Reason, _State = #seq_check{}) ->
  ok.

code_change(_OldVsn, State = #seq_check{}, _Extra) ->
  {ok, State}.

%%%===================================================================
%%% Internal functions
%%%===================================================================
check_seq(
    _Item = #data_point{fields = #{?META_FIELD := #{?SEQ_FIELD := Seq} = Meta }},
    SeqCheck0 =
      #seq_check{max_buffer_size = MaxBufferSize, seq_buffer = List, last_ts = LastTs,
        max_time_gap = MaxGap, min_eval_size = MinEvalSize, eval_size = EvalSize}) ->

%%  lager:notice("do_hande(~p, ~p)",[lager:pr(Item, ?MODULE), lager:pr(SeqCheck0, ?MODULE)]),
  Ts = faxe_time:now(),

  SeqCheck = SeqCheck0#seq_check{last_meta = Meta},
  NewList = [Seq|List],

  BufferLen = length(NewList),
  {DoCheck, EvalLen} =
    case (BufferLen >= MaxBufferSize) of
      true ->
        lager:info("****** bufferlen ~p >= max_buffer_size ~p eval_size ~p",[BufferLen, MaxBufferSize, EvalSize]),
        {true, EvalSize};
      false when ( ((Ts - LastTs) > MaxGap) andalso BufferLen >= MinEvalSize) ->
        lager:info("******* gap is > ~p ~p, eval_size (bufferlen) ~p",[MaxGap, Ts-LastTs, BufferLen]),
        {true, BufferLen};
      _ ->
        {false, EvalSize}
    end,
  NewSeqCheck =
    case DoCheck of
      true ->
        do_check(NewList, EvalLen, SeqCheck);
      false ->
        SeqCheck#seq_check{seq_buffer = NewList}
    end,
  {noreply, NewSeqCheck#seq_check{last_ts = Ts}}.


-spec do_check(SeqCheck :: #seq_check{}) -> #seq_check{}.
do_check(SeqCheck = #seq_check{seq_buffer = List, eval_size = EvalSize}) ->
  do_check(List, EvalSize, SeqCheck).

-spec do_check(SeqList :: list(), EvalLen::pos_integer(), SeqCheck::#seq_check{}) -> #seq_check{}.
do_check(SeqList, EvalLen, SeqCheck) ->
  NewSeqCheck = cancel_eval_timer(SeqCheck),
  {EvalResult, EvalRest, NSeqCheck} = eval_seq_list(SeqList, EvalLen, NewSeqCheck),
  NewSeqCheck1 = start_eval_timer(NSeqCheck),
  NewSeqCheck1#seq_check{seq_buffer = EvalResult ++ EvalRest}.

eval_seq_list(List, EvalLen, SeqCheck =
  #seq_check{last_seq = LastSeq, seq_threshold = Threshold, pool_key = PoolKey}) ->

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
%%  case length(KeyList) < EvalLen of
%%    true -> lager:warning("keylist is shorter than evalLen (~p) with ~w minseq: ~p",[EvalLen, SeqListAll, MinSeq]);
%%    false -> ok
%%  end,
  First = case MinSeq of undefined -> First0; _ -> case MinSeq+1 > Threshold of true -> 1; false -> MinSeq+1 end end,
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

send_reports([], _Key) ->
  ok;
send_reports(ReportList, PoolKey) ->
%%  lager:warning("############ seq missing: ~p",[ReportList]).
  {ok, Publisher} = mqtt_pub_pool_manager:get_connection(PoolKey),
  F = fun({Topic, Item}) ->
    Json = flowdata:to_json(Item),
    Publisher ! {publish, {Topic, Json, 1, false}}
      end,
  lists:foreach(F, ReportList).

start_eval_timer(S = #seq_check{eval_timeout = Timeout}) ->
  Timer = erlang:send_after(Timeout, self(), check),
  S#seq_check{eval_timer = Timer}.

cancel_eval_timer(S = #seq_check{eval_timer = undefined}) ->
  S;
cancel_eval_timer(S = #seq_check{eval_timer = Timer}) ->
  catch erlang:cancel_timer(Timer),
  S#seq_check{eval_timer = undefined}.