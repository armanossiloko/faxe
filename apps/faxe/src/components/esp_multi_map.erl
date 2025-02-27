%% Date: 9.1.2025
%% Ⓒ 2025 heyoka
%% @doc
% replacement for the multi_field_mapper node, example:
%
% @multi_field_mapper()
% .input_fields('module', 'alarm')                    % Target values from these fields of the INPUT
% .match_lut_fields('module_number', 'alarm_name')    % Match these fields from the look-up table (LUT)
% .add_lut_fields('alarm_text', 'alarm_type')         % Add these fields and assc. vals, from the LUT, to each point
% .lut_source(variables)                              % Use this to create the LUT
% is equivalent to:
%
% |eval(
% lambda: select_first('alarm_text', [{'module_number', "module"}, {'alarm_name', "alarm"}], variables),
% lambda: select_first('alarm_type', [{'module_number', "module"}, {'alarm_name', "alarm"}], variables)
% ).as('alarm_text', 'alarm_type')
%%
-module(esp_multi_map).
-author("Alexander Minichmair").

-behaviour(df_component).

-include("faxe.hrl").

%% API
-export([init/3, process/3, options/0, wants/0, emits/0, shutdown/1, check_options/0]).

-record(state, {
   fields         :: list(),
   match_fields   :: list(),
   select_fields  :: list(),
   lookup         :: binary(),
   as             :: undefined|list(),
   cache = #{}    :: map()

}).

options() ->
   [
      {fields, string_list},
      {match_fields, string_list},
      {as, string_list, undefined},
      {lookup, any},
      {select_fields, string_list}
   ].

check_options() ->
   [
      {same_length, [select_fields, as]}
   ].

wants() -> both.
emits() -> both.

init(_NodeId, _Ins, #{
      fields := Fields, select_fields := SelectFields,
      as := As0, lookup := Map0, match_fields := MapKeys}) ->
   JsnMap = faxe_lambda_lib:get_jsn(Map0),
   As = case As0 of undefined -> SelectFields; _ -> As0 end,
   State = #state{fields = Fields, as = As, lookup = JsnMap, match_fields = MapKeys, select_fields = SelectFields},

   {ok, all, State}.

process(_, #data_point{} = Point, State=#state{} ) ->
   {NewPoint, NewState} = map_point(Point, State),
   {emit, NewPoint, NewState};
process(_, B = #data_batch{points = Points}, State=#state{} ) ->
   FoldFun = fun(P, CState) -> map_point(P, CState) end,
   {NewPoints, NewState} = lists:mapfoldl(FoldFun, State, Points),
   {emit, B#data_batch{points = NewPoints}, NewState}.

shutdown(#state{}) ->
   ok.

%%%===================================================================
%%% Internal functions
%%%===================================================================
map_point(Point = #data_point{}, State = #state{fields = Fields}) ->
   PointValues = flowdata:fields(Point, Fields),
   {Results, NewState} = maybe_cached(PointValues, State),
   {flowdata:set_fields(Point, Results), NewState}.

maybe_cached(PointValues, S = #state{cache = Cache}) when is_map_key(PointValues, Cache) ->
   {maps:get(PointValues, Cache), S};
maybe_cached(PointValues, S = #state{as = As, lookup = Lookup, select_fields = SelectKeys, cache = Cache, match_fields = MFields}) ->
   Res = map(PointValues, Lookup, MFields, SelectKeys, As),
   NewCache = Cache#{PointValues => Res},
   {Res, S#state{cache = NewCache}}.


-spec map(PointValues :: list(), Lookup::list(), MapFields::list(), SelectKeys::list(), As::list()) -> list().
map(PointValues, Lookup, MapFields, SelectKeys, As) ->
   Where = lists:zip(MapFields, PointValues),
   case jsn:select(identity, Where, Lookup) of
      [RMap] when is_map(RMap) ->
         get_mapped_vals(SelectKeys, As, RMap, []);
      _Other ->
         lager:warning("select did not match exactly one lookup element, pls check your inputs"),
         []
   end.

get_mapped_vals([], [], _RMap, Acc) ->
   Acc;
get_mapped_vals([SelectKey|SelectKeys], [Alias|As], RMap, Acc) when is_map_key(SelectKey, RMap) ->
   Val = maps:get(SelectKey, RMap),
   NewAcc =
      case Val of
         undefined -> Acc;
         _ -> Acc++[{Alias, Val}]
      end,
   get_mapped_vals(SelectKeys, As, RMap, NewAcc);
get_mapped_vals([_|SelectKeys], [_|As], RMap, Acc) ->
   get_mapped_vals(SelectKeys, As, RMap, Acc).




