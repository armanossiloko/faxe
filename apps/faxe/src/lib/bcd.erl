-module(bcd).
-compile(export_all).

encode(N, Size) ->
  encode0(N, Size * 2, <<>>).

encode0(N, Size, Acc) when Size > 0 ->
  encode0(N div 10, Size - 1, <<(N rem 10):4, Acc/bits>>);
encode0(_, _, Acc) ->
  Acc.

decode(N, Size) ->
  case byte_size(N) of
    Size ->
      decode0(N, 0);
    _ ->
      error
  end.

decode0(<<X:4, Bin/bits>>, Acc) ->
  decode0(Bin, Acc * 10 + X);
decode0(<<>>, Acc) ->
  Acc.

%% other version of decode
decode2(Bits) ->
  list_to_integer([X+$0 || <<X:4>> <= Bits]).

