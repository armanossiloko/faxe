%%%-------------------------------------------------------------------
%%% @author heyoka
%%% @copyright (C) 2020, <COMPANY>
%%% @doc
%%% mqtt publisher pool main module
%%% @end
%%%-------------------------------------------------------------------
-module(mqtt_pub_pool_manager).

-behaviour(gen_server).

-export([start_link/0, connect/1, connection_count/1, get_connection/1, reset_counter/1, get_counter/1, get_clients/1]).
-export([init/1, handle_call/3, handle_cast/2, handle_info/2, terminate/2,
  code_change/3]).

-define(SERVER, ?MODULE).

-record(state, {
  pools_ips = #{},
  ips_pools = #{},
  ip_opts = #{},
  pool_user = #{},
  pools_up = [],
  users_waiting = #{}
}).

%%%===================================================================
%%% Spawning and gen_server implementation
%%%===================================================================
-spec connect(Opts0 :: map()) -> tuple().
connect(Opts0 = #{host := Host, port := Port}) ->
  Opts = maps:with([host, port, user, pass, ssl, qos, retain], Opts0),
  ?SERVER ! {ensure_pool, Opts, self()},
  {Host, Port}.

connection_count(Key) ->
  case ets:lookup(mqtt_pub_pools, Key) of
    [] -> 0;
    [{Key, []}] -> 0;
    [{Key, Connections}] -> length(Connections)
  end.

%%%===================================================================
%%% get a connection from a pool
%%%===================================================================
get_connection(Key) ->
  Index = get_index(Key),
  get_connection(Key, Index).

get_clients(Key) ->
  gen_server:call(?SERVER, {get_clients, Key}).

start_link() ->
  gen_server:start_link({local, ?SERVER}, ?MODULE, [], []).

init([]) ->
  erlang:process_flag(trap_exit, true),
  {ok, #state{}}.

handle_call({get_throughput, Key}, _From, State = #state{ips_pools = Pools}) ->
  Handler = maps:get(Key, Pools, undefined),
  Res =
  case Handler of
    undefined -> {ok, 0};
    _ -> gen_server:call(Handler, get_rate)
  end,
  {reply, Res, State};
handle_call({get_clients, Key}, _From, State = #state{}) ->
  PoolUsers = get_users(Key, State),
  {reply, PoolUsers, State};
handle_call(_Request, _From, State = #state{}) ->
  {reply, ok, State}.

handle_cast(_Request, State = #state{}) ->
  {noreply, State}.

handle_info({ensure_pool, #{host := Ip, port := Port} = Opts, User},
    State = #state{ips_pools = Ips, pools_ips = Pools, ip_opts = IpOpts, pool_user = PUsers,
      users_waiting = UsersWaiting, pools_up = Up}) ->
%%  lager:notice("ensure_pool for host :~p for user: ~p, current connection count: ~p",[Ip, User, connection_count(Ip)]),
  Key = {Ip, Port},
  erlang:monitor(process, User),
  NewPUsers = add_user(Key, PUsers, User),
%%  lager:info("Demand for IP ~p is ~p",[Key, IpDemand]),
  {NewState, _PoolHandler} =
  case maps:is_key(Key, Ips) of
    true ->
      {State, maps:get(Key, Ips)};
    false ->
      {ok, Pid} = mqtt_pub_pool_handler:start_link(Opts),
      %% init message counter ets table
      reset_counter(Key),
      {State#state{
        ips_pools = Ips#{Key => Pid},
        pools_ips = Pools#{Pid => Key},
        ip_opts = IpOpts#{Key => Opts}},
        Pid
      }
  end,
  UWaiting =
    case lists:member(Key, Up) of
      true ->
        User ! {mqtt_connected, Key},
        UsersWaiting;
      false ->
        case maps:is_key(Key, UsersWaiting) of
          true -> UsersWaiting#{Key => [User| maps:get(Key, UsersWaiting)]};
          false -> UsersWaiting#{Key => [User]}
        end
    end,
  {noreply, NewState#state{pool_user = NewPUsers, users_waiting = UWaiting}};

handle_info({up, Key}, State = #state{pools_up = Up, ips_pools = _Pools, users_waiting = UWaiting}) ->
%%  lager:info("pool for host ~p is UP",[Key]),
  case lists:member(Key, Up) of
    true -> {noreply, State};
    false ->
      inform_users(Key, mqtt_connected, State),
      [U ! {mqtt_connected, Key} || U <- maps:get(Key, UWaiting)],
      {noreply, State#state{pools_up = [Key|Up]}}
  end;
handle_info({down, Key}, State = #state{pools_up = Up, ips_pools = _Pools}) ->
  lager:info("pool for host ~p is DOWN",[Key]),
  inform_users(Key, mqtt_disconnected, State),
  {noreply, State#state{pools_up = lists:delete(Key, Up)}};
handle_info({'EXIT', Pid, normal}, State = #state{}) ->
  lager:warning("Pool-Handler exiting normal, no restart: ~p",[Pid]),
  NewState = remove_handler(Pid, State),
  {noreply, NewState};
%% handler exited
handle_info({'EXIT', Pid, Why}, State = #state{pools_ips = Pools, ip_opts = IpOpts})  when is_map_key(Pid, Pools) ->
  Key = maps:get(Pid, Pools),
  lager:notice("Pool-Handler ~p-~p is down: ~p",[Pid, Key, Why]),
  NState = do_remove_handler(Pid, Key, State),
  Opts = maps:get(Key, IpOpts),
  {ok, NewPid} = mqtt_pub_pool_handler:start_link(Opts),
  {noreply, NState#state{
    ips_pools = (NState#state.ips_pools)#{Key => NewPid},
    pools_ips = (NState#state.pools_ips)#{NewPid => Key}}
  };
handle_info({'EXIT', _Pid, _Why}, State = #state{}) ->
  {noreply, State};
%% pool-user is DOWN
handle_info({'DOWN', _Mon, process, Pid, _Info}, State = #state{pool_user =  PoolUsers, ips_pools = Ips}) ->
  F = fun({Key, UserList}, {L, LastIp}) ->
    UList = sets:to_list(UserList),
        case lists:member(Pid, UList) of
          true -> { [{Key, sets:from_list(lists:delete(Pid, UList), [{version, 2}])}|L] , Key};
          false -> { [{Key, sets:from_list(UList, [{version, 2}])}|L], LastIp }
        end
      end,
  {NewPoolUsers0, Key} = lists:foldl(F, {[], 0}, maps:to_list(PoolUsers)),
  NewPoolUsers = maps:from_list(NewPoolUsers0),
  case maps:is_key(Key, Ips) of
    true ->
      Handler = maps:get(Key, Ips),
      check_demand(Handler, Key, NewPoolUsers);
    false ->
      lager:warning("no pool handler found for ~p",[Key])
  end,
  {noreply, State#state{pool_user = NewPoolUsers}};
handle_info(_Req, State) ->
%%  lager:warning("Unhandled Request : ~p",[Req]),
  {noreply, State}.

terminate(_Reason, _State = #state{}) ->
  ok.

code_change(_OldVsn, State = #state{}, _Extra) ->
  {ok, State}.

%%%===================================================================
%%% Internal functions
%%%===================================================================
remove_handler(HandlerPid, State = #state{pools_ips = Pools}) when is_map_key(HandlerPid, Pools) ->
  Key = maps:get(HandlerPid, Pools),
  do_remove_handler(HandlerPid, Key, State);
remove_handler(_HandlerPid, State = #state{}) ->
  State.


do_remove_handler(HandlerPid, Key, State = #state{pools_ips = Pools, ips_pools = Ips}) ->
  NewPools = maps:without([HandlerPid], Pools),
  NewIps = maps:without([Key], Ips),
  State#state{ips_pools = NewIps, pools_ips = NewPools}.

inform_users(Key, StateMsg, State = #state{pool_user = _PoolUsers}) ->
  UsersIp = get_users(Key, State),
  [U ! {StateMsg, Key} || U <- UsersIp].

get_users(Key, #state{pool_user = PoolUsers}) when is_map_key(Key, PoolUsers) ->
  sets:to_list(maps:get(Key, PoolUsers));
get_users(_Ip, #state{pool_user = _PoolUsers}) ->
  [].

add_user(Key, AllUsers, NewUser) ->
  PoolUsers =
  case maps:is_key(Key, AllUsers) of
    true ->
      PUsersSet = maps:get(Key, AllUsers),
      sets:add_element(NewUser, PUsersSet);
    false ->
      sets:from_list([NewUser], [{version, 2}])
  end,
  AllUsers#{Key => PoolUsers}.


check_demand(Handler, Key, AllUsers) when is_map_key(Key, AllUsers) ->
  PUsersSet = maps:get(Key, AllUsers),
%%  sets:size(PUsersSet),
  case sets:size(PUsersSet) of
    0 ->
      lager:info("stop handler, no more clients left"),
      gen_server:stop(Handler),
      %% delete ets entries
      ets:delete(mqtt_pub_pools, Key),
      ets:delete(mqtt_pub_pools_index, Key),
      ets:delete(mqtt_pub_pool_cnt, Key);
    _ -> ok
  end;
check_demand(Handler, _Ip, _AllUsers) ->
  lager:info("stop handler, no more clients left"),
  gen_server:stop(Handler).



%%%===================================================================
%%% connection functions
%%%===================================================================
get_connection(Key, Index) ->
  %% bump request counter for mqtt_pub_pool_handler
  bump_counter(Key),
  case ets:lookup(mqtt_pub_pools, Key) of
    [] -> {error, no_pool_found};
    [{Key, []}] -> {error, no_connection_in_pool};
    [{Key, [Conn]}] -> {ok, Conn};
    [{Key, Connections}] ->
      NextI = next_index(length(Connections), Index),
      Worker = lists:nth(NextI, Connections),
      ets:insert(mqtt_pub_pools_index, {Key, NextI}),
%%      lager:info("~p found ~p connections, current ~p",[?MODULE, length(Connections), Worker]),
      {ok, Worker}
  end.

get_index(Key) ->
  case ets:lookup(mqtt_pub_pools_index, Key) of
    [] -> 1;
    [{Key, Index}] -> Index
  end.

next_index(L, I) when I > L -> L;
next_index(L, I) when I == L -> 1;
next_index(_L, I) -> I + 1.

%%% request counter
reset_counter(Key) ->
  ets:insert(mqtt_pub_pool_cnt, {Key, 0}).

bump_counter(Key) ->
  ets:update_counter(mqtt_pub_pool_cnt, Key, 1).

get_counter(Key) ->
  ets:update_counter(mqtt_pub_pool_cnt, Key, 0).