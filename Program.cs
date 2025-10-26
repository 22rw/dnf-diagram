using Dumpify;
using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Drawing;

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
     * Pratt-Parsing the DNF into an expression tree
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

            // Multiple operands combined with the same operators can be put in the same expression
            if (op.character == lhs.character)
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

public enum NodePosition : int
{
    AboveZero = 1,
    OnZero = 0,
    BelowZero = -1
}

public partial class Node
{
    public char character;          
    public int column;              // distance from root node as a multiple of collumns
    public int baseline;            // limit of allowed child-node placement
    public NodePosition position;   // if above, expand above, if on, expand midwise, if below expand below
    public int requiredHeight;      // for child requiredHeight += child.requiredHeight | base height = 14
    public List<Node>? childs;
    public int outputHeight;        // baseline + requiredHeight/2
    public int deepestNode;         // Deepest column this branch reaches. Used during rendering to determine the canvas width 

    public static readonly int BASE_HEIGHT = 24;
    public static readonly int COLUMN_WIDTH = 24;
}

public partial class Expression
{

    public Node ToNodeTree(int level = 0)
    {
        Node me = new Node
        {
            character = this.character,
            column = level
        };

        if (this is OperationExpression)
        {
            me.childs = new List<Node>();
            foreach (Expression child in this.operands!)
            {
                me.childs.Add(child.ToNodeTree(level + 1)!);
            }
        }

        // requiredHeight
        if (this is AtomExpression)
            me.requiredHeight = Node.BASE_HEIGHT;
        else
            me.requiredHeight = me.childs!.Sum(child => child.requiredHeight);

        if (level == 0)
            me.TraverseDown();

        return me;
    }
}

public partial class Node
{
    /*
    * Now that the node tree is a structural copy of the expression tree, we can start calculating positions for our childs
    */
    public void TraverseDown(int level = 0)
    {
        // position and baseline
        if (level == 0)
        {
            this.deepestNode = this.GetDeepestNode();
            // if we are the root node, child nodes will be spread centered (off-center by 1 with an uneven number of childs)

            this.position = NodePosition.OnZero;

            int childsBelow = this.childs!.Count / 2;
            int childsAbove = this.childs!.Count - childsBelow;

            for (int i = 0; i < this.childs!.Count; i++)
            {
                Node child = this.childs![i];
                if (i < childsBelow)
                {
                    child.position = NodePosition.BelowZero;
                    child.requiredHeight *= -1;
                }
                else
                {
                    this.childs[i].position = NodePosition.AboveZero;
                }

                /*
                 * The order in wich we place our childs is unimportant, so we just place half of them below our middle, the other half above.
                 * Starting with the first child, we continue until we hit the last child planned to be placed below.
                 * From then on, all remaining childs are placed from the middle upwards.
                 */
                if (i == 0 || i == childsBelow)
                    child.baseline = 0;
                else if (i < childsBelow)
                    child.baseline = this.childs![i - 1].baseline - this.childs![i - 1].requiredHeight;
                else
                    child.baseline = this.childs![i - 1].baseline + this.childs![i - 1].requiredHeight;

                child.outputHeight = child.baseline + child.requiredHeight / 2;

                child.TraverseDown(level + 1);
            }
        }
        else
        {
            /*
             * We are not the root node and should have got a baseline by our parent.
             * Atomic expressions (letters) don't have childs, so we only calculate child placement if we are an operation expression.
             */
            if (this.character == 'v' || this.character == '^')
            {
                Debug.WriteLine("I am a non-root operation expression. char: " + this.character + " position: " + this.position.ToString());
                for (int i = 0; i < this.childs!.Count; i++)
                {
                    Node child = this.childs![i];
                    child.position = this.position;
                    child.requiredHeight *= (int)child.position;

                    if (i == 0)
                        child.baseline = this.baseline;
                    else
                        child.baseline = this.childs![i - 1].baseline + this.childs![i - 1].requiredHeight;

                    child.outputHeight = child.baseline + child.requiredHeight / 2;
                }
            }
        }
    }

    public int GetDeepestNode() {
        int deepest = this.level;
        if(this.childs is {}) {
            foreach(Node child in this.childs)
                deepest = Math.Max(deepest, child.GetDeepestNode());
        }
        return deepest;
    }
}

class Canvas {
    public static BitMap RenderNode(Node node) {
        BitMap canvas = new BitMap();
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
            //Stack<Token> tokens = Lexer.Tokenize(input);
            //Console.WriteLine($"Parsed {tokens.Count} tokens.");
            //foreach(Token t in tokens)
            //{
            //    Console.WriteLine($"[{t.GetType()}] {t.character}");
            //}
            //Console.WriteLine();

            try
            {
                Expression expr = Expression.FromString(input);
                Node tree = expr.ToNodeTree();
                tree.Dump(members: new MembersConfig { IncludeFields = true, IncludeNonPublicMembers = true });
                Console.WriteLine();
                
            } catch (Exception e) { Console.WriteLine(e.ToString()); }
        }
    }
}

