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

%%%==================================================================
%%% public api
%%%==================================================================

%%%===================================================================
%%% Spawning and gen_server implementation
%%%===================================================================

start_link(CheckState) ->
  gen_server:start_link(?MODULE, [CheckState], []).

init([CheckState]) ->
  {ok, CheckState}.

handle_call(_Request, _From, State = #seq_check{}) ->
  {reply, ok, State}.

handle_cast(_Request, State = #seq_check{}) ->
  {noreply, State}.

handle_info({handle, Topic, Item}, State = #seq_check{}) ->
  lager:notice("handle ~p, ~p",[Topic, Item]),
  {noreply, State};
handle_info(_Info, State = #seq_check{}) ->
  {noreply, State}.

terminate(_Reason, _State = #seq_check{}) ->
  ok.

code_change(_OldVsn, State = #seq_check{}, _Extra) ->
  {ok, State}.

%%%===================================================================
%%% Internal functions
%%%===================================================================