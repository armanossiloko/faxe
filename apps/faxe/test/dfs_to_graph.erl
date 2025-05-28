%%%-------------------------------------------------------------------
%%% @author heyoka
%%% @copyright (C) 2020, <COMPANY>
%%% @doc
%%%
%%% @end
%%% Created : 01. Sep 2020 09:52
%%%-------------------------------------------------------------------
-module(dfs_to_graph).
-author("heyoka").

-include_lib("eunit/include/eunit.hrl").

compile_helper(DfsScriptFile) ->
  {_, GraphMap} = faxe_dfs:file(DfsScriptFile, []),
  GraphMap.

unknown_opt_test() ->
  application:set_env(faxe, dfs, [{script_path, "apps/faxe/test/dfs/"}]),
  Expected = {error,"Unknown option 'ls_mem' for node 'debug'"},
  ?assertEqual(Expected, compile_helper("unknown_options_test.dfs")).

%%batch_test() ->
%%  Expected =   #{edges =>
%%  [{<<"value_emitter1">>,1,<<"batch2">>,1,[]},
%%    {<<"batch2">>,1,<<"debug3">>,1,[]}],
%%    nodes =>
%%    [{<<"debug3">>,esp_debug,
%%      #{'_name' => <<"debug3">>,level => <<"warning">>}},
%%      {<<"batch2">>,esp_batch,
%%        #{'_name' => <<"batch2">>,size => 5,
%%          timeout => <<"5750ms">>}},
%%      {<<"value_emitter1">>,esp_value_emitter,
%%        #{'_name' => <<"value_emitter1">>,align => false,
%%          batch_size => 5,every => <<"8000ms">>,
%%          fields => [<<"val">>],
%%          format => undefined,jitter => <<"3700ms">>,
%%          mode => <<"random">>,type => point}}]}
%%
%%  ,
%%  ?assertEqual(Expected, compile_helper("batch_test.dfs")).



bridge_test() ->
  Expected =   #{nodes =>
  [{<<"amqp_publish5">>,esp_amqp_publish,
    #{port => undefined,exchange => <<"x_root_fanout">>,
      user => <<"rabbitmq-cluster-user">>,ssl => true,
      host => <<"15.45.48.1">>,persistent => false,
      pass => <<"dfwefwef8ePI78we">>,vhost => <<"/">>,
      routing_key => <<"some.crazy.topic.this.is">>,
      qos => 0,vhost_prefix => <<"undefined">>,
      '_name' => <<"amqp_publish5">>,
      '_stop_idle' => false,'_idle_time' => <<"5m">>,
      '_stop_when' => undefined,
      routing_key_lambda => undefined,
      routing_key_field => undefined}},
    {<<"amqp_publish4">>,esp_amqp_publish,
      #{port => undefined,exchange => <<"x_root_fanout">>,
        user => <<"rabbitmq-cluster-user">>,ssl => false,
        host => <<"some.other_amqp_host">>,
        persistent => false,pass => <<"adfafdwewef3">>,
        vhost => <<"/">>,
        routing_key => <<"some.crazy.topic.this.is">>,
        qos => 0,vhost_prefix => <<"undefined">>,
        '_name' => <<"amqp_publish4">>,
        '_stop_idle' => false,'_idle_time' => <<"5m">>,
        '_stop_when' => undefined,
        routing_key_lambda => undefined,
        routing_key_field => undefined}},
    {<<"amqp_publish3">>,esp_amqp_publish,
      #{port => undefined,exchange => <<"x_root_fanout">>,
        user => <<"rabbitmq-cluster-user">>,ssl => false,
        host => <<"some.amqp_host">>,persistent => false,
        pass => <<"asdf323232">>,vhost => <<"/">>,
        routing_key => <<"some.crazy.topic.this.is">>,
        qos => 0,vhost_prefix => <<"undefined">>,
        '_name' => <<"amqp_publish3">>,
        '_stop_idle' => false,'_idle_time' => <<"5m">>,
        '_stop_when' => undefined,
        routing_key_lambda => undefined,
        routing_key_field => undefined}},
    {<<"debug2">>,esp_debug,
      #{message => <<>>,level => <<"notice">>,
        where => undefined,'_name' => <<"debug2">>,
        '_stop_idle' => false,'_idle_time' => <<"5m">>,
        '_stop_when' => undefined}},
    {<<"mqtt_subscribe1">>,esp_mqtt_subscribe,
      #{port => 1883,user => <<"undefined">>,ssl => false,
        host => <<"10.102.1.102">>,pass => <<"undefined">>,
        as => undefined,
        topic => <<"some/crazy/topic/this/is">>,qos => 1,
        client_id => undefined,dt_field => <<"ts">>,
        dt_format => <<"millisecond">>,
        include_topic => true,topic_as => <<"topic">>,
        '_name' => <<"mqtt_subscribe1">>,
        '_stop_idle' => false,topics => undefined,
        remove_meta_field => true,'_idle_time' => <<"5m">>,
        '_stop_when' => undefined}}],
    edges =>
    [{<<"debug2">>,1,<<"amqp_publish5">>,1,[]},
      {<<"debug2">>,1,<<"amqp_publish4">>,1,[]},
      {<<"debug2">>,1,<<"amqp_publish3">>,1,[]},
      {<<"mqtt_subscribe1">>,1,<<"debug2">>,1,[]}]}
  ,
  ?assertEqual(Expected, compile_helper("mqtt_amqp_bridge_test.dfs")).


bridge_expr_test() ->
  Expected =  #{nodes =>
  [{<<"amqp_publish5">>,esp_amqp_publish,
    #{port => undefined,exchange => <<"x_root_fanout">>,
      user => <<"rabbitmq-cluster-user">>,ssl => true,
      host => <<"15.45.48.1">>,persistent => false,
      pass => <<"dfwefwef8ePI78we">>,vhost => <<"/">>,
      routing_key => <<"some.crazy.topic.this.is">>,
      qos => 0,vhost_prefix => <<"undefined">>,
      '_name' => <<"amqp_publish5">>,
      '_stop_idle' => false,'_idle_time' => <<"5m">>,
      '_stop_when' => undefined,
      routing_key_lambda => undefined,
      routing_key_field => undefined}},
    {<<"amqp_publish4">>,esp_amqp_publish,
      #{port => undefined,exchange => <<"x_root_fanout">>,
        user => <<"rabbitmq-cluster-user">>,ssl => false,
        host => <<"some.other_amqp_host">>,
        persistent => false,pass => <<"adfafdwewef3">>,
        vhost => <<"/">>,
        routing_key => <<"some.crazy.topic.this.is">>,
        qos => 0,vhost_prefix => <<"undefined">>,
        '_name' => <<"amqp_publish4">>,
        '_stop_idle' => false,'_idle_time' => <<"5m">>,
        '_stop_when' => undefined,
        routing_key_lambda => undefined,
        routing_key_field => undefined}},
    {<<"amqp_publish3">>,esp_amqp_publish,
      #{port => undefined,exchange => <<"x_root_fanout">>,
        user => <<"rabbitmq-cluster-user">>,ssl => false,
        host => <<"some.amqp_host">>,persistent => false,
        pass => <<"asdf323232">>,vhost => <<"/">>,
        routing_key => <<"some.crazy.topic.this.is">>,
        qos => 0,vhost_prefix => <<"undefined">>,
        '_name' => <<"amqp_publish3">>,
        '_stop_idle' => false,'_idle_time' => <<"5m">>,
        '_stop_when' => undefined,
        routing_key_lambda => undefined,
        routing_key_field => undefined}},
    {<<"debug2">>,esp_debug,
      #{message => <<>>,level => <<"notice">>,
        where => undefined,'_name' => <<"debug2">>,
        '_stop_idle' => false,'_idle_time' => <<"5m">>,
        '_stop_when' => undefined}},
    {<<"mqtt_subscribe1">>,esp_mqtt_subscribe,
      #{port => 1883,user => <<"undefined">>,ssl => false,
        host => <<"10.102.1.102">>,pass => <<"undefined">>,
        as => undefined,
        topic => <<"some/crazy/topic/this/is">>,qos => 1,
        client_id => undefined,dt_field => <<"ts">>,
        dt_format => <<"millisecond">>,
        include_topic => true,topic_as => <<"topic">>,
        '_name' => <<"mqtt_subscribe1">>,
        '_stop_idle' => false,topics => undefined,
        remove_meta_field => true,'_idle_time' => <<"5m">>,
        '_stop_when' => undefined}}],
    edges =>
    [{<<"debug2">>,1,<<"amqp_publish5">>,1,[]},
      {<<"debug2">>,1,<<"amqp_publish4">>,1,[]},
      {<<"debug2">>,1,<<"amqp_publish3">>,1,[]},
      {<<"mqtt_subscribe1">>,1,<<"debug2">>,1,[]}]}
  ,
  ?assertEqual(Expected, compile_helper("script_expr_test.dfs")).

