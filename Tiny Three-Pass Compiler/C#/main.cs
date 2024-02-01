namespace CompilerKata
{
    using System;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Collections.Generic;

    public class Compiler
    {
        public class Source
        {
            private readonly Dictionary<string, int> _args = new Dictionary<string, int>();
            private readonly IReadOnlyList<string> _tokens;
            private int _pos;
            
            public Source(IReadOnlyList<string> tokens)
            {
                _tokens = tokens;
                _pos = tokens.Count - 1;

                for (var idx = 1; _tokens[idx] != "]"; ++idx) _args[_tokens[idx]] = _args.Count;
            }

            public string Next() => _tokens[_pos--];

            public string Current => _tokens[_pos];

            public int GetArg(string name) => _args[name];
        }

        public Ast Factor(Source source)
        {
            var value = source.Next();
            if (value == ")")
            {
                var result = Expression(source);
                source.Next();
                return result;
            }
            return int.TryParse(value, out var number) ?new UnOp("imm", number) : new UnOp("arg", source.GetArg(value));
        }

        public Ast Term(Source source)
        {
            var factor = Factor(source);
            return "*/".Contains(source.Current) ? new BinOp(source.Next(), Term(source), factor) : factor;
        }

        public Ast Expression(Source source)
        {
            var term = Term(source);
            return "+-".Contains(source.Current) ? new BinOp(source.Next(), Expression(source), term) : term;
        }

        private void Pop(List<string> code)
        {
            if (code.Last() == "PU") code.RemoveAt(code.Count - 1); else code.Add("PO");
        }

        private void GenerateAsm(Ast ast, List<string> code)
        {
            if(ast is UnOp unOp)
            {
                switch(unOp.op())
                {
                    case "imm": code.Add($"IM {unOp.n()}"); break;
                    case "arg": code.Add($"AR {unOp.n()}"); break;
                }

                code.Add("PU");
            }
            else if (ast is BinOp binOp)
            {
                GenerateAsm(binOp.a(), code);
                GenerateAsm(binOp.b(), code);

                Pop(code);
                code.Add("SW");
                Pop(code);
                switch (binOp.op())
                {
                    case "+": code.Add("AD"); break;
                    case "-": code.Add("SU"); break;
                    case "*": code.Add("MU"); break;
                    case "/": code.Add("DI"); break;
                }

                code.Add("PU");
            }
        }

        public Ast pass1(string prog) => Expression(new Source(tokenize(prog)));

        public Ast pass2(Ast ast)
        {
            if (ast is BinOp binOp)
            {
                var a = pass2(binOp.a());
                var b = pass2(binOp.b());

                if(a is UnOp uOp1 && b is UnOp uOp2 && a.op() == "imm" && b.op() == "imm")
                {
                    switch(binOp.op())
                    {
                        case "*": return new UnOp("imm", uOp1.n() * uOp2.n());
                        case "/": return new UnOp("imm", uOp1.n() / uOp2.n());
                        case "+": return new UnOp("imm", uOp1.n() + uOp2.n());
                        case "-": return new UnOp("imm", uOp1.n() - uOp2.n());
                    }
                }

                return new BinOp(binOp.op(), a, b);
            }

            return ast;
        }

        public List<string> pass3(Ast ast)
        {
            var result = new List<string>();
            GenerateAsm(ast, result);
            Pop(result);
            return result;
        }

        private List<string> tokenize(string input)
        {
            var tokens = new List<string>();
            var rgxMain = new Regex("\\[|\\]|[-+*/=\\(\\)]|[A-Za-z_][A-Za-z0-9_]*|[0-9]*(\\.?[0-9]+)");
            var matches = rgxMain.Matches(input);
            foreach (Match m in matches) tokens.Add(m.Groups[0].Value);
            return tokens;
        }
    }
}