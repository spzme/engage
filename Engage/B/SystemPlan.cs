﻿using Engage.C;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Engage.B
{
    public class SystemPlan
    {
        private static readonly Dictionary<string, string> RealNames = new Dictionary<string, string>()
        {
            {"string","System.String" },
            {"number","System.Int32" },
        };

        public string NS;
        public string TopType;
        private Dictionary<string, TypePlan> Types = new Dictionary<string, TypePlan>();
        public static Dictionary<string, string> TypeAliases = new Dictionary<string, string>();
        public HashSet<string> BoolFlags = new HashSet<string>();
        public HashSet<string> IntFlags = new HashSet<string>();
        public Dictionary<string, List<TokenPlan>> Tokens = new Dictionary<string, List<TokenPlan>>();
        public Dictionary<string, List<HandlerPlan>> Handlers = new Dictionary<string, List<HandlerPlan>>();

        public SystemPlan(string ns)
        {
            NS = ns;
        }

        public static string Dealias(string name)
            => TypeAliases.ContainsKey(name) ? RealNames[TypeAliases[name]] : name;

        internal TypePlan GetTypePlan(string name)
        {
            if (Types.ContainsKey(name))
                return Types[name];
            if (TypeAliases.ContainsKey(name))
                return GetTypePlan(TypeAliases[name]);
            if (RealNames.ContainsKey(name))
                return new TypePlan() { Name = RealNames[name] };
            Console.WriteLine($"[ B ] Failed to get a type plan for '{name}'");
            return null;
        }

        internal bool HasType(string name)
            => Types.ContainsKey(name);

        internal void AddType(string n, string super, bool silent = false)
        {
            if (String.IsNullOrEmpty(n))
                return;
            if (Types.Count == 0)
            {
                TopType = n;
                Console.WriteLine($"[A2B] Top type is assumed to be {TopType}");
            }
            if (Types.ContainsKey(n))
            {
                if (!silent)
                    Console.WriteLine($"[A2B] Cannot add type '{n}' the second time");
                return;
            }
            TypePlan tp = new TypePlan();
            tp.Name = n;
            tp.Super = super;
            Console.WriteLine($"[A2B] Added type '{n}' to the plan");
            Types[tp.Name] = tp;
        }

        public IEnumerable<C.CsClass> GenerateDataClasses()
            => Types.Values
                .Where(t => !t.IsList)
                .Select(t => t.GenerateClass(NS));

        public C.CsClass GenerateParser()
        {
            var p = new C.CsClass();
            p.NS = NS;
            p.Name = "Parser";
            p.Super = "BaseParser";
            p.AddUsing("EngageRuntime");
            p.AddUsing("System");
            p.AddUsing("System.Collections.Generic");
            if (BoolFlags.Count > 0)
                p.AddField(String.Join(", ", BoolFlags), "bool", isPublic: false);
            if (IntFlags.Count > 0)
                p.AddField(String.Join(", ", IntFlags), "int", isPublic: false);
            // token types
            var tt = new C.CsEnum();
            tt.IsPublic = false;
            tt.Name = "TokenType";
            tt.Values.Add("TUndefined");
            tt.Values.Add("TEOF");
            tt.Values.AddRange(Tokens.Keys.Where(t => t != "skip").Select(t => "T" + t));
            p.AddInner(tt);
            // parser constructor
            var pc = new C.CsConstructor();
            pc.InheritFromBase = true;
            pc.AddArgument("input", "string");
            p.AddConstructor(pc);
            // the parse function
            var pf = new C.CsMethod();
            pf.Name = "Parse";
            pf.RetType = "object";
            pf.AddCode("string ERROR = \"\"");
            pf.AddCode("TokenType type");
            pf.AddCode("string lexeme");
            List<C.CsStmt> loop = new List<C.CsStmt>();
            var pl = new C.CsComplexStmt();
            pl.Before = "do";
            pl.After = "while (type != TokenType.TEOF)";

            // main parsing loop: begin
            pl.AddCode("var _token = NextToken();");
            pl.AddCode("lexeme = _token.Item2;");
            pl.AddCode("type = _token.Item1;");

            var swType = new C.CsSwitchCase();
            swType.Expression = "type";

            var UsedTokens = new HashSet<string>();
            UsedTokens.Add("skip");

            foreach (var hpk in Handlers.Keys)
            {
                List<C.CsStmt> branchType = new List<C.CsStmt>();
                if (hpk == "EOF")
                    branchType.Add(new C.CsSimpleStmt("Flush()"));
                if (Handlers[hpk].Count == 1)
                {
                    foreach (var action in Handlers[hpk][0].Recipe)
                    {
                        if (action != null)
                            action.GenerateAbstractCode(branchType);
                        else
                            Console.WriteLine($"[IR] Warning: no action to handle '{hpk}'/{Handlers[hpk][0].ReactOn.Value}");
                    }
                }
                else
                {
                    var swLex = new C.CsSwitchCase();
                    // much faster to switch-case on a char than on a string
                    bool matchChar = Handlers[hpk].Select(hp => hp.ReactOn.Value).All(v => v.Length == 1);
                    if (matchChar)
                        swLex.Expression = "lexeme[0]";
                    else
                        swLex.Expression = "lexeme";
                    // Need this dance because there may be different actions for the same token with different guards
                    Dictionary<TokenPlan, Tuple<List<string>, List<List<HandleAction>>>> resortedHandlers = new Dictionary<TokenPlan, Tuple<List<string>, List<List<HandleAction>>>>();
                    foreach (var hp in Handlers[hpk])
                    {
                        if (!resortedHandlers.ContainsKey(hp.ReactOn))
                            resortedHandlers[hp.ReactOn] = new Tuple<List<string>, List<List<HandleAction>>>(new List<string>(), new List<List<HandleAction>>());
                        resortedHandlers[hp.ReactOn].Item1.Add(hp.GuardFlag);
                        resortedHandlers[hp.ReactOn].Item2.Add(hp.Recipe);
                    }
                    List<TokenPlan> resortedKeys = resortedHandlers.Keys.ToList();
                    resortedKeys.Sort((x, y) => y.Value.Length - x.Value.Length);
                    foreach (var key in resortedKeys)
                        GenerateLexBranch(swLex, hpk, resortedHandlers[key].Item1, resortedHandlers[key].Item2, key, matchChar);

                    //var list = Handlers[hpk];
                    //list.Sort((x, y) => y.ReactOn.Value.Length - x.ReactOn.Value.Length);
                    //foreach (var hp in list)
                    //{
                    //    GenerateLexBranch(swLex, hpk, hp.GuardFlag, hp.Recipe, hp.ReactOn, matchChar);

                    //}
                    branchType.Add(swLex);
                }
                swType.Branches["TokenType.T" + hpk] = branchType;
                UsedTokens.Add(hpk);
            }
            foreach (var t in Tokens.Keys)
            {
                if (!UsedTokens.Contains(t))
                    Console.WriteLine($"[B2C] unused token {t}");
                foreach (B.TokenPlan tok in Tokens[t])
                {
                    if (!tok.Special)
                        continue;
                    List<C.CsStmt> branchType = new List<C.CsStmt>();
                    switch (tok.Value)
                    {
                        case "number":
                            branchType.Add(new C.CsSimpleStmt($"Push(System.Int32.Parse(lexeme));"));
                            break;

                        case "string":
                            branchType.Add(new C.CsSimpleStmt($"Push(lexeme);"));
                            break;
                    }
                    swType.Branches["TokenType.T" + t] = branchType;
                }
            }

            pl.AddCode(swType);
            var abend = new C.CsComplexStmt();
            abend.Before = "if (!System.String.IsNullOrEmpty(ERROR))";
            abend.AddCode("Console.WriteLine(\"Parser error: \" + ERROR);");
            abend.AddCode("return null;");
            pl.AddCode(abend);
            // main parsing loop: end

            pf.AddCode(pl);
            pf.AddCode($"if (Main.Peek() is {TopType})", "return Main.Pop()");
            pf.AddCode("return null"); // TODO!!!
            p.AddMethod(pf);

            // other methods
            GenerateTokeniser(p);

            return p;
        }

        private void GenerateLexBranch(CsSwitchCase swLex, string hpk, List<string> guardFlags, List<List<HandleAction>> recipes, TokenPlan reactOn, bool matchChar)
        {
            List<C.CsStmt> branchLex = new List<C.CsStmt>();
            //Console.WriteLine($"[IR] in '{hpk}', handle {hp.ReactOn.Value}");
            var kwd = "if";
            bool onlyWraps = true;
            for (int i = 0; i < guardFlags.Count; i++)
            {
                CsComplexStmt ifst = new C.CsComplexStmt();
                ifst.Before = $"{kwd} ({guardFlags[i]})";
                List<CsStmt> target = String.IsNullOrEmpty(guardFlags[i]) ? branchLex : ifst.Code;
                foreach (var action in recipes[i])
                {
                    if (!(action is WrapOne))
                        onlyWraps = false;
                    if (action != null)
                        action.GenerateAbstractCode(target);
                    else
                        Console.WriteLine($"[IR] Warning: no action to handle '{hpk}'/{reactOn.Value}");
                }
                kwd = "else if";
                if (!String.IsNullOrEmpty(guardFlags[i]))
                    branchLex.Add(ifst);
            }
            string flags;
            if (guardFlags.Count == 1)
                flags = "flag " + guardFlags[0] + " is not";
            else
                flags = "neither of the flags " + String.Join(", ", guardFlags) + " are";
            if (!onlyWraps)
            if (guardFlags.Any(f => !String.IsNullOrEmpty(f)))
                branchLex.Add(new CsComplexStmt("else", $"ERROR = \"{flags} lifted when expected\""));
            if (matchChar)
                swLex.Branches["'" + reactOn.Value + "'"] = branchLex;
            else
                swLex.Branches['"' + reactOn.Value + '"'] = branchLex;
        }

        private void GenerateTokeniser(C.CsClass cls)
        {
            var skipmark = new List<string>();

            var tok = new C.CsMethod();
            tok.IsPublic = false;
            tok.Name = "NextToken";
            tok.RetType = "Tuple<TokenType, string>";

            // init phase
            tok.AddCode("TokenType t = TokenType.TUndefined;");
            tok.AddCode("string s = \"\";");
            // EOF phase
            tok.AddCode(new C.CsComplexStmt("if (pos >= input.Length)", "return new Tuple<TokenType, string>(TokenType.TEOF, \"\")"));
            // skip
            if (Tokens.ContainsKey("skip"))
            {
                string cond = String.Join(" || ", Tokens["skip"].Select(t => $"input[pos] == '{t.Value}'"));
                tok.AddCode(new C.CsComplexStmt($"while (pos < input.Length && ({cond}))", "pos++"));
                Tokens["skip"].ForEach(t => skipmark.Add(t.Value));
            }
            else
                Console.WriteLine($"[IR] It is suspicious that there are no tokens of type 'skip'");
            // EOF after skip
            tok.AddCode(new C.CsComplexStmt("if (pos >= input.Length)", "return new Tuple<TokenType, string>(TokenType.TEOF, \"\")"));
            // mark
            if (Tokens.ContainsKey("mark"))
            {
                Tokens["mark"].ForEach(t => skipmark.Add(t.Value));
                GenerateBranches("mark", tok, null);
            }
            else
                Console.WriteLine($"[IR] It is suspicious that there are no tokens of type 'word'");
            // word
            if (Tokens.ContainsKey("word"))
                GenerateBranches("word", tok, skipmark);
            else
                Console.WriteLine($"[IR] It is suspicious that there are no tokens of type 'word'");
            // number etc
            foreach (var tt in Tokens.Keys)
            {
                if (tt == "skip" || tt == "word" || tt == "mark")
                    continue;
                GenerateBranches(tt, tok, skipmark);
            }
            tok.AddCode("return new Tuple<TokenType, string>(t, s);");

            cls.AddMethod(tok);
        }

        // Precondition: Tokens.Contains(token_name)
        private void GenerateBranches(string token_name, C.CsMethod method, List<string> skipmark)
        {
            if (!Tokens.ContainsKey(token_name))
                return;
            foreach (var tm in Tokens[token_name])
                if (tm.Special)
                    method.AddCode(GenerateBranchSpecialMatch(tm.Value, token_name));
                else
                    method.AddCode(GenerateBranchPreciseMatch(tm.Value, token_name, skipmark));
        }

        private C.CsComplexStmt GenerateBranchSpecialMatch(string value, string type)
        {
            switch (value)
            {
                case "number":
                    return GenerateBranchNumberMatch(type);

                case "string":
                    return GenerateBranchStringMatch(type);

                default:
                    Console.WriteLine($"[IR] Cannot generate a match for '{value}'");
                    return null;
            }
        }

        private C.CsComplexStmt GenerateBranchStringMatch(string type)
        {
            string cond = "";
            if (Tokens.ContainsKey("skip"))
                foreach (var t in Tokens["skip"])
                    cond += $" && input[pos] != '{t.Value}'";
            C.CsComplexStmt ifst = new C.CsComplexStmt();
            ifst.Before = "else";
            ifst.AddCode($"t = TokenType.T{type}");
            ifst.AddCode($"while (pos < input.Length{cond})", "s += input[pos++]");
            return ifst;
        }

        private C.CsComplexStmt GenerateBranchNumberMatch(string type)
        {
            C.CsComplexStmt ifst = new C.CsComplexStmt();
            string cond = string.Join(" || ", "0123456789".Select(c => $"input[pos] == '{c}'"));
            ifst.Before = $"else if ({cond})";
            ifst.AddCode($"t = TokenType.T{type}");
            ifst.AddCode($"while (pos < input.Length && ({cond}))", "s += input[pos++]");
            return ifst;
        }

        private C.CsComplexStmt GenerateBranchPreciseMatch(string value, string type, List<string> skipmark)
        {
            int len = value.Length;
            C.CsComplexStmt ifst = new C.CsComplexStmt();
            string cond;
            if (len > 1)
                cond = $"pos + {len - 1} < input.Length";
            else
                cond = "";
            for (int i = 0; i < len; i++)
                cond += $" && input[pos + {i}] == '{value[i]}'";
            if (cond.StartsWith(" && "))
                cond = cond.Substring(4);

            // either EOF or next is skip or mark
            if (skipmark != null && skipmark.Count > 0)
                cond = $"({cond}) && (pos + {len} == input.Length || {String.Join(" || ", skipmark.Select(c => $"input[pos + {len}] == '{c}'"))})";

            cond = cond.Replace(" + 0", "");
            ifst.Before = $"else if ({cond})";
            ifst.AddCode($"t = TokenType.T{type}");
            ifst.AddCode($"s = \"{value}\"");
            if (len > 1)
                ifst.AddCode($"pos += {len}");
            else
                ifst.AddCode("pos++");
            return ifst;
        }
    }
}