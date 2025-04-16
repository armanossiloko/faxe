%% Date: 23.07.2021
%% Mongo DB find
%% Ⓒ 2021 heyoka
%%
-module(esp_mongo_query).
-author("Alexander Minichmair").

-include("faxe.hrl").

-behavior(df_component).
%% API
-export([
   init/3, process/3, options/0, handle_info/2,
   metrics/0, check_options/0]).

-record(state, {
   host :: string(),
   port :: non_neg_integer(),
   selector :: map(),

   user :: string(), %% Schema
   pass :: string(), %%
   database :: iodata(),
   collection :: binary(),
   as :: binary(),
   client,
   client_ref,
   db_opts,
   every,
   align = false,
   timer,
   fn_id
}).

-define(DB_OPTIONS, #{
   timeout => 3000
}).


options() ->
   [
      {host, string},
      {port, integer, 27017},
      {user, string, <<>>},
      {pass, string, <<>>},
      {database, string},
      {collection, string},
      {query, string, <<"{}">>}, %% json string
      {as, binary, undefined},
      {time_field, string, <<"ts">>},
      {every, duration, undefined},
      {align, is_set, false}
   ].

check_options() ->
   [
      {func, query,
         fun(Selector) ->
            case catch(jiffy:decode(Selector, [return_maps])) of
               S when is_map(S) orelse is_list(S) -> true;
               _ -> false
            end
         end,
         <<" seems not to be valid json">>}
   ].

%% @todo figure out how to get the byte-size of the data
metrics() ->
   [
%%      {?METRIC_READING_TIME, histogram, [slide, 60], "Network time for sending a message."},
%%      {?METRIC_BYTES_READ, meter, []}
   ].

init(NodeId, _Inputs, #{host := Host0, port := Port, user := User, every := Every, as := As,
      pass := Pass, query := JsonString, align := Align, database := DB, collection := Collection}) ->

   %% we need to trap exists form the result cursors
   process_flag(trap_exit, true),
   Host = binary_to_list(Host0),

   Query = jiffy:decode(JsonString, [return_maps]),

   DBOpts = [{host, Host}, {port, Port}, {login, User}, {password, Pass}, {database, DB}],
   connection_registry:reg(NodeId, Host, Port, <<"mongodb">>),
   State = #state{host = Host, port = Port, user = User, pass = Pass, selector = Query, database = DB,
      db_opts = DBOpts, every = Every, align = Align, fn_id = NodeId, collection = Collection, as = As},
%%   erlang:send_after(0, self(), reconnect),
   {error, node_removed, State}.
%%   {ok, all, State}.

process(_, _, State) ->
   {ok, State}.

handle_info(_, State) ->
   {ok, State}.
