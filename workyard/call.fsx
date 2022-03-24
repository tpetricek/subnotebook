#nowarn "40"
#r "nuget: FSharp.Data"
open FSharp.Data

FSharp.Data.Http.RequestString("http://localhost:7101/test")

let code_ = """
def magic_f ():
  import numpy as np
  import pandas as pd
  import json

  def read_eurostat_file(fn):
    raw = pd.read_csv(fn,sep='\t',na_values=[': ',': z',': c'])
    info = \
      raw[raw.columns[0]].str.split(pat=',',expand=True) \
        .set_axis(raw.columns[0].split(','), axis=1,inplace=False) \
        .rename(columns={'geo\\time':'geo'})
    filtered = raw.drop([raw.columns[0]], axis=1)
    return info.join(filtered)

  avia_file = "C:/Tomas/Public/tpetricek/histogram/traffic/raw/avia_paoc.tsv"
  rail_file = "C:/Tomas/Public/tpetricek/histogram/traffic/raw/rail_pa_total.tsv"
  avia = read_eurostat_file(avia_file)
  rail = read_eurostat_file(rail_file)

  avia_ps = \
    avia.loc[(avia["tra_meas"] == "CAF_PAS") & (avia["schedule"] == "TOT") & \
      (avia["tra_cov"] == "TOTAL") & (avia["geo"] != "EU27") & (avia["geo"] != "EU28")]  \
      .set_index("geo")

  rail_ps = \
    rail.loc[(rail["unit"] == "THS_PAS") & (rail["geo"] != "EU27") &(rail["geo"] != "EU28")] \
      .set_index("geo")

  results = {}
  results['avia']=avia_ps.to_json(orient='records')
  results['rail']=rail_ps.to_json(orient='records')
  return results
"""

open System.Collections.Generic

type Type = 
  | Object of IDictionary<string, string -> string list -> string * Type>
  | StringType
  | NumberType

type Expr = 
  | Variable of string
  | Global of string
  | String of string
  | Number of int
  | Chain of Expr * (string * Expr list) list

type Command = 
  | Let of string * Expr
  
let rec iframe = 
  [ "columns", fun inst [n] ->
      sprintf "%s.columns[%s]" inst n,
      StringType
    "drop_columns", fun inst [cols] -> 
      sprintf "%s.drop(%s, axis=1)" inst cols,
      iframe
    "head", fun inst [] -> 
      sprintf "%s.head()" inst,
      iframe ] |> dict |> Object

let sframe = 
  [ "load", fun _ [fn; sep; na] -> 
      sprintf "pd.read_csv(%s,sep=%s,na_values=%s)" fn sep na,
      iframe ] |> dict |> Object

let slist =
  [ "create", fun _ args -> 
      sprintf "[%s]" (String.concat "," args),
      Object(dict []) ] |> dict |> Object

let objs = 
  [ "frame", sframe
    "list", slist ] |> dict

let rec formatExpr (vars:IDictionary<_, _>) e : string * Type =
  match e with
  | Variable(v) -> vars.[v]
  | Global o -> o, objs.[o]
  | String s -> sprintf "'%s'" s, StringType
  | Number n -> sprintf "%d" n, NumberType
  | Chain(inst, chain) ->
      let inst, typ = formatExpr vars inst
      chain |> List.fold (fun (inst, typ) (meth, args) ->
        match typ with 
        | Object o ->           
            let args = args |> List.map (formatExpr vars >> fst)
            if not (o.ContainsKey meth) then failwithf "Object '%s' does not contain method '%s'" inst meth
            o.[meth] inst args
        | _ -> failwith "Cannot invoke things on strings") (inst, typ)

let formatProg p =
  let vars = System.Collections.Generic.Dictionary<_, _>()
  [ for (Let(v, e)) in p do
      let fe, t = formatExpr vars e
      vars.Add(v, (v, t))
      yield sprintf "  %s = %s" v fe
      yield sprintf "  results['%s'] = json.loads(%s.to_json(orient='split'))" v v ]
  |> String.concat "\n"

let prog = [
  // raw = pd.read_csv(fn,sep='\t',na_values=[': ',': z',': c'])
  Let("raw", Chain(Global "frame", [
    "load", [ 
        String "C:/Tomas/Public/tpetricek/histogram/traffic/raw/rail_pa_total.tsv"
        String "\\t" 
        Chain(Global "list", ["create", [ String ": "; String ": z"; String ": c" ]])
    ]
  ]))
  // filtered = raw.drop([raw.columns[0]], axis=1)
  Let("filtered", Chain(Variable "raw", [
    "drop_columns", [
      Chain(Global "list", ["create", [ 
        Chain(Variable "raw", [ "columns", [ Number 0 ] ])
      ]])
    ]
  ]))
  // info = raw[raw.columns[0]].str.split(pat=',',expand=True) \
  //   .set_axis(raw.columns[0].split(','), axis=1,inplace=False) \
  //   .rename(columns={'geo\\time':'geo'})
  // info.join(filtered)
]

let code = formatProg prog
let wrapped = sprintf """
def magic_f ():
  import numpy as np
  import pandas as pd
  import json

  results = {}
%s
  return results""" code

printfn "%s" wrapped

let json = 
  FSharp.Data.JsonValue.Record 
    [| "code", FSharp.Data.JsonValue.String wrapped
       "main", FSharp.Data.JsonValue.String "magic_f" |]
  
let res = FSharp.Data.Http.RequestString("http://localhost:7101/eval", body=FSharp.Data.HttpRequestBody.TextRequest(json.ToString()))
FSharp.Data.JsonValue.Parse(res)
