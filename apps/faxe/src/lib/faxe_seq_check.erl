%%%-------------------------------------------------------------------
%%% @author heyoka
%%% @copyright (C) 2025, <COMPANY>
%%% @doc
%%% @end
%%%-------------------------------------------------------------------
-module(faxe_seq_check).

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
  {noreply, age_check(S)};
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
    #data_point{fields = #{?META_FIELD := #{?SEQ_FIELD := Seq} = Meta}, ts = Ts} = Item,
    SeqCheck0 =
      #seq_check{max_buffer_size = MaxBufferSize, seq_buffer = List, eval_size = _EvalSize}) ->

%%  lager:notice("do_hande(~p, ~p)",[lager:pr(Item, ?MODULE), lager:pr(SeqCheck0, ?MODULE)]),
  NewList = [{Seq, Ts}|List],
  check_late(Seq, Item, SeqCheck0),
  SeqCheck = SeqCheck0#seq_check{last_meta = Meta, seq_buffer = NewList},
  BufferLen = length(NewList),
  NewSeqCheck =
  case (BufferLen >= MaxBufferSize) of
    true ->
      {_Tmy, Res} = timer:tc(fun() -> do_check(SeqCheck) end),
%%      lager:info("****** bufferlen ~p >= max_buffer_size ~p eval_size ~p, buffer size: ~p, check done in ~pmy",
%%        [BufferLen, MaxBufferSize, EvalSize, faxe_util:bytes(NewList), Tmy]),
      Res;
    false ->
      SeqCheck
  end,
  {noreply, NewSeqCheck#seq_check{last_ts = Ts}}.

age_check(SeqCheck = #seq_check{seq_buffer = List, min_eval_size = MinSize, max_age = MaxAge}) ->
  Now = faxe_time:now(),
  MinTs = Now - MaxAge,
  SeqListAll = orddict:to_list(orddict:from_list(List)),
  MinSeq = get_min_seq(SeqCheck),
  F =
  fun
    ({Seq, Ts}, {undefined, SList, RList}) when Ts < MinTs, Seq > MinSeq ->
      {Seq, SList ++ [Seq], RList};
    ({Seq, Ts}, {First, SList, RList}) when Ts < MinTs, Seq > MinSeq ->
      {First, SList ++ [Seq], RList};
    (E, {First, SList, RList}) ->
      {First, SList, RList++[E]}
  end,
  {FirstSeq, EvalList, RestList} = lists:foldl(F, {undefined, [], []}, SeqListAll),
  CheckRec =
  case length(EvalList) >= MinSize of
    true ->
      eval_seq_list(MinSeq, FirstSeq, EvalList, RestList, SeqCheck);
    false ->
%%      lager:notice("EvalList: ~p is not >= ~p",[EvalList, MinSize]),
      SeqCheck
  end,
  start_eval_timer(CheckRec).

%%-spec do_check(SeqList :: list(), EvalLen::pos_integer(), SeqCheck::#seq_check{}) -> #seq_check{}.
do_check(SeqCheck = #seq_check{seq_threshold = Threshold, eval_size = EvalLen, seq_buffer = List}) ->
  NewSeqCheck = cancel_eval_timer(SeqCheck),
  %% get the ordered list of all
  SeqListAll = orddict:to_list(orddict:from_list(List)),
  %% split the list and at the same time, get the keys from the left list
  MinSeq = get_min_seq(SeqCheck),
  {First0, KeyList, RList} =
    case catch split_get_keys(EvalLen, SeqListAll, MinSeq) of
      {[First01|_] = KeyList1, RList1} -> {First01, KeyList1, RList1};
      _What ->
%%        lager:warning("called split_get_keys with ~p, ~w ~p(~p) got ~w",[EvalLen, SeqListAll, MinSeq, Threshold, What]),
        {0, [], []}
    end,
  NSeqCheck = eval_seq_list(MinSeq, First0, KeyList, RList, NewSeqCheck),
  start_eval_timer(NSeqCheck).

eval_seq_list(MinSeq, First0, KeyList, RList,
    SeqCheck=#seq_check{pool_key = PoolKey, seq_threshold = Threshold, seq_buffer = Buffer}) ->
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
%%  LastValid = last_valid(CheckList, MissingList, SeqCheck#seq_check.last_seen),
%%   lager:notice("~nminkey: ~p ||| check ~w |||| seqlist: ~w |||| missing: ~w, remaining: ~w,  first: ~w, last: ~w, last_seq ~w, last_valid ~w",
%%      [MinSeq, CheckList, KeyList, MissingList, RemainingList, First, Last, LastSeq1, LastValid]),
  faxe_seq_check_manager:count(SeqCheck#seq_check.key, Last-First, length(MissingList)),
  spawn(fun() -> report_seq(MissingList, CheckList++RemainingList, SeqCheck, PoolKey) end),
  RemainingList1 = [{K, proplists:get_value(K, Buffer)} || K <- RemainingList],

  SeqCheck#seq_check{seq_buffer = RemainingList1 ++ RList, last_seq = LastSeq1}.

%%last_valid(CheckList, MissingList, Previous) ->
%%  case CheckList -- MissingList of
%%    [] -> Previous;
%%    L -> lists:last(L)
%%  end.

get_min_seq(#seq_check{last_seq = LastSeq, seq_threshold = Threshold}) ->
  case LastSeq of
    undefined -> undefined;
    Other when Other >= Threshold -> 0;
    _ -> LastSeq
  end.

-spec split_get_keys(N :: pos_integer(), L::list(), Min::undefined|list()) -> {list(), list(), list()}.
split_get_keys(N, L, Min) ->
  split_get_keys(N, L, {[], [], []}, Min).

-spec split_get_keys(non_neg_integer(), L::list(), tuple(), _Min::undefined|list()) -> tuple().
split_get_keys(0, L, {_R, K, Skipped}, _Min) ->
  {lists:reverse(K, []), L++Skipped};
split_get_keys(_, [], {_R, K, Skipped}, _Min) ->
  {lists:reverse(K, []), Skipped};
split_get_keys(N, [{HK, _Ts}=H|T], {R, K, Skipped}, undefined) ->
  split_get_keys(N-1, T, {[H|R],[HK|K], Skipped}, undefined);
split_get_keys(N, [{HK, _Ts}=H|T], {R, K, Skipped}, Min) when HK > Min ->
  split_get_keys(N-1, T, {[H|R],[HK|K], Skipped}, Min);
split_get_keys(N, [H|T], {R, K, Skipped}, Min) ->
  split_get_keys(N, T, {R, K, [H|Skipped]}, Min).


check_late(_Seq, _Item, #seq_check{last_seq = undefined}) -> ok;
check_late(Seq, Item, SeqCheck = #seq_check{last_seq = LastSeq, seq_threshold = Max})
    when Seq < LastSeq
    %% avoid reporting, when seq number rolled over to 1 already
    andalso (LastSeq - Seq) < (Max / 5) ->
  report_late_arrival(Item, SeqCheck);
check_late(_S, _Item, _SeqCheck) -> ok.


%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
report_late_arrival(#data_point{fields = Fs, ts = Ts},
    #seq_check{pool_key = PoolKey, report_topic_late = Topic, last_seq = Last}) ->

  DP = flowdata:new(),
  Point = DP#data_point{fields = #{<<"late_point">> => Fs#{<<"ts">> => Ts}, <<"last_seq_checked">> => Last}},
  send_reports([{Topic, Point}], PoolKey).

report_seq(MissingList, KeyList, SeqCheck, PoolKey) ->
  Reports = build_check_report(MissingList, KeyList, SeqCheck),
  send_reports(Reports, PoolKey).

build_check_report([], _, #seq_check{report_topic_mask = _Topic}) ->
  [];
build_check_report(MissingList, KeyList, #seq_check{report_topic = SendTopic, last_meta = Meta}) ->
  Seen = KeyList -- MissingList,
  F =
    fun(SeqKey) ->
      DP = flowdata:new(),
      {Prev, Next} = related_seq(Seen, SeqKey),
      Fields = Meta#{<<"seq_prev">> => Prev, <<"seq_next">> => Next, <<"seq">> => SeqKey},
      {SendTopic, DP#data_point{fields = Fields}}
    end,
  Reports = lists:map(F, MissingList),
%%  [lager:warning("send report ~p",[P]) || P <- Reports],
  Reports.

related_seq([First], _Seq) ->
  {First, 0};
related_seq([_First|_] = List, Seq) ->
  F = fun
        (Ele, {Min, Max}) ->
          NewMin =
            case Ele < Seq andalso Ele > Min of
              true -> Ele;
              false -> Min
            end,
          NewMax =
            case Ele > Seq andalso Ele < Max of
              true -> Ele;
              false -> Max
            end,
          {NewMin, NewMax}
      end,
  lists:foldl(F, {0, lists:last(List)}, List);
related_seq(_, _Seq) ->
  {0, 0}.



send_reports([], _Key) ->
  ok;
send_reports(ReportList, PoolKey) ->
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