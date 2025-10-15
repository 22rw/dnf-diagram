using System;

// --- Lexer related stuff ---

public class Token
{
    public char character;
}

class Operator : Token { }
class Negator: Token { }
class Opener : Token { }
class Closer : Token { }
class Atom : Token { }
class Eof : Token { }

enum Operation : int
{
    ATOM = -1,
    NOT = '!',
    AND = '^',
    OR = 'v'
}

enum NodePosition : int
{
    AboveZero = 1,
    OnZero = 0,
    BelowZero = -1
}

class Node
{
    public Operation operation;     // if gate its corresponding op, if input/atom -1
    public int baseline;            // limit of allowed child-node placement
    public NodePosition position;   // if above, expand above, if on, expand midwise, if below expand below
    public int requiredHeight;      // for child requiredHeight += child.requiredHeight | base height = 14
    public List<Node>? children;
    public int outputHeight;        // baseline + requiredHeight/2
}

public class Lexer
{
    public static Stack<Token> Tokenize(string dnf)
    {
        Stack<Token> tokens = new Stack<Token>();
        foreach (char c in dnf.Replace(" ", "").ToCharArray())
        {
            tokens.Push(c switch
            {
                'v' => new Operator { character = c },
                '^' => new Operator { character = c },
                '(' => new Opener { character = c },
                ')' => new Closer { character = c },
                '!' => new Negator { character = c },
                _ => new Atom { character = c },
            });
        }
        tokens.Push(new Eof());
        var list = tokens.ToList();
        //list.Reverse(); Converting to list already reverses item order, but reversing without list conversion isn't possible, so this is enough
        return new Stack<Token>(list);
    }
}

// --- Expression related stuff ---

public partial class Expression
{
    public bool invert;
    public char character;
    public List<Expression>? operands;
}

class AtomExpression : Expression { }
class OperationExpression : Expression { }

public partial class Expression
{    
    /*
     * A
     * 
     * AvB
     * 
     * !A
     * 
     * A^!B
     * 
     * A v B v C
     * 
     * Av(A^B)
     * 
     * !(AvB)
     * 
     * (A^B) v !(A^B) v (A^!C) 
     *  
     */
    public static Expression Parse(Stack<Token> tokens, bool invert = false)
    {
        Expression lhs = tokens.Pop() switch
        {
            Atom a => new AtomExpression { character = a.character },
            Negator => tokens.Peek() switch
            {
                Atom => new AtomExpression { character = tokens.Pop().character, invert = true },
                Opener => Parse(tokens, true),
                Token t => TokenError(t)
            },
            Opener => Parse(tokens, invert),
            Token t => TokenError(t)
        };

        while(true)
        {
            // TODO: Peek instead of Pop?? 
            Token op = new Token();
            switch (tokens.Peek())
            {
                case Eof:
                    return lhs;
                case Closer:
                    tokens.Pop();
                    if (lhs.operands == null) lhs.invert = invert;
                    return lhs;
                case Operator:
                    op = tokens.Pop();
                    break;
                case Token t:
                    TokenError(t);
                    break;
            }

            Expression rhs = tokens.Pop() switch
            {
                Atom a => new AtomExpression { character = a.character },
                Negator => tokens.Peek() switch
                {
                    Atom => new AtomExpression { character = tokens.Pop().character, invert = true },
                    Opener => Parse(tokens, true),
                    Token t => TokenError(t)
                },
                Opener => Parse(tokens, invert),
                Token t => TokenError(t)
            };

            if (lhs.character == op.character)
            {
                lhs.operands!.Add(rhs);
            }
            else
            {
                lhs = new OperationExpression { character = op.character, invert = tokens.Peek().GetType() != typeof(Operator) ? invert : false, operands = new List<Expression> { lhs, rhs } };
            }
        }
    }

    private static Expression TokenError(Token t)
    {
        throw new Exception($"bad token: [{t.GetType()}] '{t.character}'");
    }

    public static Expression FromString(string s)
    {
        return Parse(Lexer.Tokenize(s));
    }

    public override string ToString()
    {
        string s = string.Empty;
        if (this.operands?.Count > 0)
        {
            s = "(" + string.Join(this.character, this.operands) + ")";
        }
        else
        {
            s = this.character.ToString();
        }
        if(this.invert)
        {
            s = "!" + s;
        }
        return s;
    }
}

// --- Main method ---

class Program 
{
    static void Main() 
    {
        string input;
        while((input = Console.ReadLine() ?? "") != "quit")
        {
            Stack<Token> tokens = Lexer.Tokenize(input);
            Console.WriteLine($"Parsed {tokens.Count} tokens.");
            foreach(Token t in tokens)
            {
                Console.WriteLine($"[{t.GetType()}] {t.character}");
            }
            Console.WriteLine();

            try
            {
                Expression expr = Expression.FromString(input);
                Console.WriteLine("Parsed expression:");
                Console.WriteLine(expr.ToString());
                Console.WriteLine();
            } catch (Exception e) { Console.WriteLine(e.ToString()); }
        }
    }
}

