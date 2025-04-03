%%%-------------------------------------------------------------------
%%% @author heyoka
%%% @copyright (C) 2025, <COMPANY>
%%% @doc serialize force delete action
%%% @end
%%%-------------------------------------------------------------------
-module(flow_deleter).

-behaviour(gen_server).

-export([start_link/0, do/1]).
-export([init/1, handle_call/3, handle_cast/2, handle_info/2, terminate/2,
  code_change/3]).

-include("faxe.hrl").

-define(SERVER, ?MODULE).
-define(MAX_RETRIES, 3).
-define(RETRY_INTERVAL, 2000).

-record(state, {}).

do(Flow = #task{}) ->
  ?SERVER ! {{delete_flow, Flow}, 1}.
%%%===================================================================
%%% Spawning and gen_server implementation
%%%===================================================================

start_link() ->
  gen_server:start_link({local, ?SERVER}, ?MODULE, [], []).

init([]) ->
  {ok, #state{}}.

handle_call(_Request, _From, State = #state{}) ->
  {reply, ok, State}.

handle_cast(_Request, State = #state{}) ->
  {noreply, State}.

handle_info({{delete_flow, _Flow=#task{name = Name}}, NumTried}, State = #state{}) when NumTried > ?MAX_RETRIES ->
  lager:notice("cannot force delete flow: ~p with ~p retries",[Name, ?MAX_RETRIES]),
  {noreply, State};
handle_info({{delete_flow, Flow=#task{name = Name}} = Req, NumTried}, State = #state{}) ->
  case catch faxe:force_delete_task(Flow) of
    ok -> ok;
    Other ->
      lager:notice("force delete flow '~p' failed with ~p, retry",[Name, Other]),
      erlang:send_after(?RETRY_INTERVAL, self(), {Req, NumTried+1})
  end,
  {noreply, State}.

terminate(_Reason, _State = #state{}) ->
  ok.

code_change(_OldVsn, State = #state{}, _Extra) ->
  {ok, State}.

%%%===================================================================
%%% Internal functions
%%%===================================================================
