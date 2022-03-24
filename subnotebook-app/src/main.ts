import axios from 'axios';

// Types
type MemberType = (inst:string, args:string[]) => TypedValue
type TypeString = { kind: "string" }
type TypeNumber = { kind: "number" }
type TypeObject = {
  kind: "object"
  members: { [name:string]: MemberType }
}
type Type = TypeString | TypeNumber | TypeObject

// Expressions
type ExprSymbol = { kind:"symbol", name:string }
type ExprNumber = { kind:"number", value:number }
type ExprString = { kind:"string", value:string }
type ExprChain = { 
  kind:"chain", 
  instance:Expr
  chain:{operation:string, args:Expr[]}[]
}
type Expr = ExprSymbol | ExprNumber | ExprString | ExprChain

type Command = { variable:string, expression:Expr }
type Program = Command[]

type TypedValue = {code:string, type:Type}

const t = {
  object: (m:{ [name:string]: MemberType }): Type =>
    ({kind:"object", members:m }),
  val: (c:string, t:Type): TypedValue => 
    ({code:c, type:t})
}

let inone : Type = t.object({})

let iframe : Type = t.object({
  "head": (inst, args) => 
    t.val(`${inst}.head()`, iframe)
})

let sframe : Type = t.object({
  "load": (inst, args) => 
    t.val(`pd.read_csv(${args[0]},sep=${args[1]},na_values=${args[2]})`, iframe)
})

let slist : Type = t.object({
  "create": (inst, args) => 
    t.val(`[${args.join(",")}]`, inone)
})

const objs : {[name:string]:Type} = {
  "frame" : sframe,
  "list": slist
}

function formatExpr(vars:{[name:string]:TypedValue}, e:Expr) : TypedValue {
  switch(e.kind) {
    case "symbol": 
      if (vars[e.name]) return {"code":e.name, "type":vars[e.name].type};
      if (objs[e.name]) return t.val("!!!", objs[e.name])
      throw "Variable or global not defined!"; 
    case "string": return { code:`'${e.value}'`, type:{kind:"string"} }
    case "number": return { code:e.value.toString(), type:{kind:"number"} }
    case "chain": 
      let current = formatExpr(vars, e.instance)
      for(const c of e.chain) {
        switch(current.type.kind) {
          case "object":
            let args = c.args.map(a => formatExpr(vars, a).code)
            if (!current.type.members[c.operation]) throw "Member not defined!"
            let memberType = current.type.members[c.operation]
            current = memberType(current.code, args)
            break;
          default: throw `Not an object but ${current.type.kind}!`
        }
      }
      return current;
  }
}

function formatProg(p:Program) {
  let variables : {[name:string]:TypedValue} = {}
  let code = ""
  for(const c of p) {
    let fmt = formatExpr(variables, c.expression)
    variables[c.variable] = fmt
    code += `  ${c.variable} = ${fmt.code}\n`;
    code += `  results['${c.variable}'] = json.loads(${c.variable}.to_json(orient='split'))\n`;
  }
  return code;
}

const e = {
  symbol: (n:string) : Expr => ({ kind: "symbol", name: n }),
  str: (s:string) : Expr => ({ kind: "string", value: s }),
  chain: (i:Expr, ch:{operation:string, args:Expr[]}[]) : Expr => 
    ({ kind:"chain", instance:i, chain:ch }),
  member: (n:string, args:Expr[]) =>
    ({operation:n, args:args}),
  cmd: (v:string, e:Expr) : Command => ({variable: v, expression: e})
}

// let raw = frame.load("c:/...", "\t", list.create(": ", ": z", ": c"))
// let raw = 
//    (frame 
//      (load "c:/..." "\t")
//      head )

let prog : Program = [
  // let raw = frame.load("c:/...", "\t", list.create(": ", ": z", ": c"))
  e.cmd("raw", e.chain(e.symbol("frame"), [
    e.member("load", [
      e.str("data/rail.tsv"),
      e.str("\\t"),
      e.chain(e.symbol("list"), [e.member("create", [ e.str(": "), e.str(": z"), e.str(": c") ])])
    ])
  ])),
  // let small = raw.head()
  e.cmd("small", e.chain(e.symbol("raw"), [
    e.member("head", [])
  ]))
]

let code = formatProg(prog);
let wrapped = `
def magic_f ():
  import numpy as np
  import pandas as pd
  import json

  results = {}
${code}
  return results`

console.log(wrapped)

axios.post("http://localhost:7101/eval",
  { code: wrapped,
    main: "magic_f" }).then(r => {
      var html = "";
      for(const c of prog) {
        html += `<h1>${c.variable}</h1>`
        html += JSON.stringify(c.expression)
        var value = r.data[c.variable]
        html += "<table class='table'><thead><tr>"
        for(var col of value.columns) html += `<th>${col}</th>`
        html += "</tr></thead><tbody>"
        for(var row of value.data) {
          html += "<tr>"
          for(var col of row) html += `<td>${col}</td>`
          html += "</tr>"
        } 
        html += "</tbody></table>"
      }
      document.getElementById("output").innerHTML = html;
    })
