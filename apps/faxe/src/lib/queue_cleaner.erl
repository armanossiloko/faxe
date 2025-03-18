%%%-------------------------------------------------------------------
%%% @author heyoka
%%% @copyright (C) 2025, <COMPANY>
%%% @doc
%%% @end
%%%-------------------------------------------------------------------
-module(queue_cleaner).

-behaviour(gen_server).

-export([start_link/0]).
-export([init/1, handle_call/3, handle_cast/2, handle_info/2, terminate/2,
  code_change/3]).

-include("faxe.hrl").

-define(SERVER, ?MODULE).

-record(state, {
  %% amqp clients per connection
  clients     = #{} :: map(),
  clients_status     = #{} :: map(),
  %% queues to delete per connection
  send_buffer = #{} :: map(),
  clients_reverse = #{} :: map()
}).
-export([add_q/2, get_qs/1, clean/1]).

-define(TABLE, flow_amqp_queues).


%% add an entry from flow to queue
-spec add_q(tuple(), {binary(), map()}) -> any().
add_q({FlowId, NodeId}, {QueueName, Opts0}) ->
  Opts1 = maps:with([host, port, user, pass, vhost, ssl], Opts0),
  Opts = consumer_config(Opts1),
%%  lager:info("add queue ~p for flow ~p",[{QueueName, Opts}, {FlowId, NodeId}]),
  Rec = #flow_amqp_queues{flow_id = FlowId, node_id = NodeId, queue_name = QueueName, amqp_opts = Opts},
  NewQs =
    case get_qs(FlowId) of
      [] -> [Rec];
      L when is_list(L) ->
        CleanedList = lists:filter(fun(#flow_amqp_queues{queue_name = QName}) -> QueueName /= QName end, L),
        [Rec|CleanedList]
    end,
  faxe_db:save_flow_amqp_queue(NewQs).

%% get a list of queue defs created by the given flow
-spec get_qs(FlowId :: binary()) -> [#flow_amqp_queues{}].
get_qs(FlowId) ->
  case faxe_db:get_flow_amqp_queues(FlowId) of
    [] -> [];
    Qs when is_list(Qs) -> Qs
  end.


clean(FlowId) ->
  ?SERVER ! {clean, FlowId}.
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

handle_info({amqp_connected, Client}, #state{clients_reverse = ClientsRev, send_buffer = Buf, clients_status = Stat} = State)
    when is_map_key(Client, ClientsRev) ->

  %% check buffer
  COpts = maps:get(Client, ClientsRev),
  BuffEntries = maps:get(COpts, Buf, []),
  [do_delete(Client, QName) || #flow_amqp_queues{queue_name = QName} <- BuffEntries],
  NewBuffer = maps:without([COpts], Buf),
  NewStat = Stat#{COpts => true},
  {noreply, State#state{send_buffer = NewBuffer, clients_status = NewStat}};
handle_info({amqp_disconnected, Client}, #state{clients_status = Stat, clients_reverse = ClientsRev} = State) ->
  COpts = maps:get(Client, ClientsRev, undefined),
  NewStat =
  case COpts of
    undefined -> Stat;
    _ -> Stat#{COpts => false}
  end,
  {noreply, State#state{clients_status = NewStat}};
handle_info({clean, FlowId}, State = #state{}) ->
  Qs = get_qs(FlowId),
  NewState = delete(Qs, State),
  faxe_db:delete_flow_amqp_queues(FlowId),
  {noreply, NewState};
handle_info({'DOWN', _MRef, process, Pid, Info}, #state{clients_reverse = ClientsRev, clients_status = Stat} = State)
    when is_map_key(Pid, ClientsRev) ->
  ClientOpts = maps:get(Pid, ClientsRev),
  lager:notice("MQ-Client ~p is 'DOWN' for reason ~p, remove from pool",[ClientOpts, Info]),
  NewClients = maps:without([ClientOpts], State#state.clients),
  NewClientsRev = maps:without([Pid], ClientsRev),
  NewStat = maps:without([ClientOpts], Stat),
  {ok, State#state{clients = NewClients, clients_reverse = NewClientsRev, clients_status = NewStat}};
handle_info(_Info, State = #state{}) ->
  {noreply, State}.

terminate(_Reason, _State = #state{clients_reverse = CRev}) ->
  [catch rmq_consumer:stop(C) || C <- maps:keys(CRev)],
  ok.

code_change(_OldVsn, State = #state{}, _Extra) ->
  {ok, State}.

delete([], State) ->
  State;
delete([#flow_amqp_queues{amqp_opts = Opts, queue_name = QName} = Q |QList], State=#state{send_buffer = Buf}) ->
  NewS =
  case get_client(Opts, State) of
    {false, NewState, _Client} ->
      NewBuffer = add_to_buf(Buf, Opts, Q),
      NewState#state{send_buffer = NewBuffer};
    {true, NewStateF, C} ->
      do_delete(C, QName),
      NewStateF
  end,
  delete(QList, NewS).


do_delete(Client, QName) ->
  rmq_consumer:delete_queue(Client, QName).

add_to_buf(Buf, Key, Value) when is_map_key(Key, Buf) ->
  Entry = maps:get(Key, Buf),
  Buf#{Key => [Value|Entry]};
add_to_buf(Buf, Key, Value) ->
  Buf#{Key => [Value]}.


%%%===================================================================
%%% Internal functions
%%%===================================================================
get_client(COpts, S = #state{clients = Clients, clients_status = Stat}) when is_map_key(COpts, Clients) ->
  case maps:get(COpts, Stat, false) of
    true -> {true, S, maps:get(COpts, Clients)};
    false -> {false, S, undefined}
  end;
get_client(COpts, S =#state{clients = Clients, clients_reverse = CRev, clients_status = Stat}) ->
  case start_client(COpts, S) of
    undefined ->
      {false, S, undefined};
    NewClient when is_pid(NewClient) ->
      NewClients = maps:put(COpts, NewClient, Clients),
      NewRev = maps:put(NewClient, COpts, CRev),
      CStat = Stat#{COpts => false},
      {false, S#state{clients = NewClients, clients_reverse = NewRev, clients_status = CStat}, NewClients}
  end.


-spec start_client(Opts :: map(), State::#state{}) -> pid()|undefined.
start_client(Opts, #state{}) ->
  case catch rmq_consumer:start_monitor(self(), Opts) of
    {ok, Pid, _NewClient} ->
      Pid;
    What -> lager:warning("Error when starting rmq consumer : ~p",[What]), undefined
  end.

consumer_config(Opts = #{host := _Host, port := _Port, user := _User, pass := _Pass, vhost := VHost, ssl := _UseSSL}) ->
  Config =
    [
      {callback, ?SERVER},
      {vhost, VHost},
      {setup_queue, false}
    ],
  Props = carrot_util:proplists_merge(
    maps:to_list(Opts) ++ [{ssl_opts, faxe_config:get_amqp_ssl_opts()}], Config),
  Props.