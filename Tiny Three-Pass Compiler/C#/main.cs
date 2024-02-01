namespace CompilerKata
 {
    using System;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Collections.Generic;
   
    public class Compiler
    {
        #region Pass1

        public Ast pass1(string program)
        {
            var tokens = tokenize(program);

            var argumentsEnd = tokens.IndexOf("]");

            var argsMap = tokens.Take(argumentsEnd).Skip(1).Select((x, i) => (x, i)).ToDictionary(x => x.x, x => x.i);

            var parenthesesStack = new Stack<ParenthesesContext>(new[] { new ParenthesesContext() });

            for (var i = argumentsEnd + 1; i < tokens.Count; i++)
            {
                var token = tokens[i];

                switch (token)
                {
                    case "(":
                        parenthesesStack.Push(new());
                        continue;
                    case ")":
                    {
                        var currentContext = parenthesesStack.Pop();
                        var parentContext = parenthesesStack.Peek();

                        parentContext.AstStack.Push(FinalizeContext(currentContext));
                        continue;
                    }
                }

                var context = parenthesesStack.Peek();

                if (IsOperation(token))
                {
                    if (context.OperationsStack.TryPeek(out var lastOp))
                        // a*b + 2, where current op is "+"
                        if (GetPriority(lastOp) <= GetPriority(token))
                        {
                            var right = context.AstStack.Pop();
                            var left = context.AstStack.Pop();
                            context.OperationsStack.Pop();
                            context.AstStack.Push(new BinOp(lastOp, left, right));
                        }

                    context.OperationsStack.Push(token);
                }
                else
                {
                    var isConstant = IsConstant(token);
                    var opType = isConstant ? "imm" : "arg";
                    var opParam = isConstant ? int.Parse(token) : argsMap[token];
                    context.AstStack.Push(new UnOp(opType, opParam));
                }
            }

            return FinalizeContext(parenthesesStack.Pop());
        }

        private static Ast FinalizeContext(ParenthesesContext innerContext)
        {
            var ast = innerContext.AstStack.Reverse().ToList();
            var ops = innerContext.OperationsStack.Reverse().ToList();

            if (ops.Count > 1 && GetPriority(ops[^2]) > GetPriority(ops[^1]))
            {
                var left = ast[^2];
                var right = ast[^1];
                var op = ops[^1];
                ast[^2] = new BinOp(op, left, right);
                ast.RemoveAt(ast.Count - 1);
                ops.RemoveAt(ops.Count - 1);
            }

            var contextResult = ast[0];

            for (var i = 0; i < ops.Count; i++)
            {
                var op = ops[i];
                var right = ast[i + 1];
                contextResult = new BinOp(op, contextResult, right);
            }

            return contextResult;
        }

        private static int GetPriority(string op)
        {
            return op switch
            {
                "+" => 3,
                "-" => 3,
                "*" => 2,
                "/" => 2,
                "(" => 1,
                ")" => 1,
                _ => throw new ArgumentOutOfRangeException(nameof(op), op, null)
            };
        }

        private static readonly Regex OperationRegex = new("^[-+*/=\\(\\)]$");
        private static readonly Regex ConstantRegex = new("^[0-9]*(\\.?[0-9]+)$");

        private static bool IsOperation(string s)
        {
            return OperationRegex.IsMatch(s);
        }

        private static bool IsConstant(string s)
        {
            return ConstantRegex.IsMatch(s);
        }
                private static readonly Regex TokensRegex = new("\\[|\\]|[-+*/=\\(\\)]|[A-Za-z_][A-Za-z0-9_]*|[0-9]*(\\.?[0-9]+)");

        private List<string> tokenize(string input)
        {
            var tokens = new List<string>();
            var rgxMain = TokensRegex;
            var matches = rgxMain.Matches(input);
            foreach (Match m in matches) tokens.Add(m.Groups[0].Value);
            return tokens;
        }

        private record ParenthesesContext(Stack<string> OperationsStack, Stack<Ast> AstStack)
        {
            public ParenthesesContext() : this(new(), new()) { }
        }

        #endregion

        #region Pass2

        public Ast pass2(Ast ast)
        {
            if (ast is not BinOp binOp) return ast;

            var left = pass2(binOp.a());
            var right = pass2(binOp.b());
            if (left is UnOp leftUnOp && leftUnOp.op() == "imm" && right is UnOp rightUnOp && rightUnOp.op() == "imm")
                return new UnOp("imm", CalculateValue(ast.op(), leftUnOp.n(), rightUnOp.n()));

            return new BinOp(ast.op(), left, right);
        }

        private static int CalculateValue(string op, int a, int b)
        {
            return op switch
            {
                "+" => a + b,
                "-" => a - b,
                "*" => a * b,
                "/" => a / b,
                _ => throw new ArgumentOutOfRangeException(nameof(op), op, null)
            };
        }

        #endregion

        #region Pass3

        public List<string> pass3(Ast ast)
        {
            if (ast is UnOp unOp) // Corner case if whole AST is a constant
                return new() { GetUnOpAsm(unOp) };

            if (ast is not BinOp binOp) throw new("🤨");

            var left = binOp.a();
            var right = binOp.b();

            return ((left, right) switch
            {
                (UnOp a, UnOp b) => new[]
                {
                    GetUnOpAsm(b),
                    "SW",
                    GetUnOpAsm(a),
                    GetBinOpAsm(binOp.op())
                },
                (BinOp a, UnOp b) => pass3(a)
                    .Append("SW")
                    .Append(GetUnOpAsm(b))
                    .Append("SW") // Could be optimized, include only when operation is + or *
                    .Append(GetBinOpAsm(binOp.op())),
                (UnOp a, BinOp b) => pass3(b)
                    .Append("SW")
                    .Append(GetUnOpAsm(a))
                    .Append(GetBinOpAsm(binOp.op())),
                (BinOp a, BinOp b) => pass3(a)
                    .Append("PU")
                    .Concat(pass3(b))
                    .Append("SW")
                    .Append("PO")
                    .Append(GetBinOpAsm(binOp.op())),
                (_, _) => throw new("🤨")
            }).ToList();
        }

        private static string GetUnOpAsm(UnOp unOp)
        {
            return (unOp.op() == "imm" ? "IM " : "AR ") + unOp.n();
        }

        private static string GetBinOpAsm(string op)
        {
            return op switch
            {
                "+" => "AD",
                "-" => "SU",
                "*" => "MU",
                "/" => "DI",
                _ => throw new ArgumentOutOfRangeException(nameof(op), op, null)
            };
        }

        #endregion
    }
}