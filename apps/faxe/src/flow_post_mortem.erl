%%%-------------------------------------------------------------------
%%% @author heyoka
%%% @copyright (C) 2025, <COMPANY>
%%% @doc
%%% @end
%%%-------------------------------------------------------------------
-module(flow_post_mortem).

-behaviour(gen_server).

-export([start_link/0, get_stopped_flows/0]).
-export([init/1, handle_call/3, handle_cast/2, handle_info/2, terminate/2,
  code_change/3]).

-include("faxe.hrl").

-define(SERVER, ?MODULE).

-define(INTERVAL, proplists:get_value(report_interval, faxe_config:get_sub(flow_health, post_mortem), 60*60*1000)).
%% flow health message "STOPPED"
-define(MESSAGE, <<"{\"status\":2}">>).
-define(QOS, 0).
-define(RETAINED, false).

-record(state, {interval, base_topic, host}).

%%%===================================================================
%%% Spawning and gen_server implementation
%%%===================================================================

start_link() ->
  gen_server:start_link({local, ?SERVER}, ?MODULE, [], []).

init([]) ->
  MqttOpts = #{base_topic := BaseTopic0, host := Host} = faxe_flow_observer:mqtt_opts(),
  BaseTopic = faxe_flow_observer:topic_base(BaseTopic0),
  %% use mqtt publisher pool !!
  mqtt_pub_pool_manager:connect(MqttOpts),
  ReportInterval = ?INTERVAL,
  start_timer(ReportInterval),
  {ok, #state{interval = ReportInterval, base_topic = BaseTopic, host = Host}}.

handle_call(_Request, _From, State = #state{}) ->
  {reply, ok, State}.

handle_cast(_Request, State = #state{}) ->
  {noreply, State}.

handle_info(send, State = #state{interval = Interval, base_topic = BaseTopic, host = Host}) ->
  start_timer(Interval),
  [send(topic(FlowId, BaseTopic), Host) || FlowId <- get_stopped_flows()],
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
start_timer(Interval) ->
  erlang:send_after(Interval, self(), send).

get_stopped_flows() ->
  [T#task.name || T <- faxe:list_permanent_tasks(), T#task.is_running /= true].

topic(FlowId,  BaseTopic) ->
  faxe_util:build_topic([BaseTopic, FlowId]).

send(Topic, Host) ->
  case mqtt_pub_pool_manager:get_connection(Host) of
    {ok, Publisher}  ->
      Publisher ! {publish, {Topic, ?MESSAGE, ?QOS, ?RETAINED}};
    _Other ->
      ok
  end.
