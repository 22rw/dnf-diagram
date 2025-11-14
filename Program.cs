using Dumpify;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Web;

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
                '⊕' => new Operator { character = c },
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
    public bool isOpen;
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
                    lhs.invert = invert;
                    lhs.isOpen = false;
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
            
            if (op.character == lhs.character && lhs.isOpen)
                // Multiple operands combined with the same operators can be put in the same expression
                lhs.operands!.Add(rhs);
            else
                lhs = new OperationExpression { character = op.character, isOpen = true, operands = new List<Expression> { lhs, rhs } };
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
            s = "(" + string.Join(this.character, this.operands) + ")";
        else
            s = this.character.ToString();

        if(this.invert)
            s = "!" + s;

        return s;
    }
}

public partial class Node
{
    public char character;
    public bool invert;
    public int column;              // distance from root node as a multiple of collumns
    public int requiredHeight;      // for child requiredHeight += child.requiredHeight | base height = 14
    public int outputHeight;        // baseline + requiredHeight/2
    public int deepestNode;         // Deepest column this branch reaches. Used during rendering to determine the canvas width 

    public int from;
    public int to;

    public List<Node>? childs;

    public static readonly int BASE_SIZE = 64;
}

public partial class Expression
{

    public Node ToNodeTree(int level = 0)
    {
        Node me = new Node
        {
            character = this.character,
            column = level,
            deepestNode = level,
            invert = this.invert,
        };
        
        // collect child nodes
        if (this is OperationExpression)
        {
            me.childs = new List<Node>();
            foreach (Expression child in this.operands!)
            {
                var childNode = child.ToNodeTree(level + 1)!;
                me.childs.Add(childNode);
                me.deepestNode = Math.Max(me.deepestNode, childNode.deepestNode);
            }
        }

        // requiredHeight
        if (this is AtomExpression)
            me.requiredHeight = Node.BASE_SIZE;
        else
            me.requiredHeight = me.childs!.Sum(child => child.requiredHeight);

        // initialize all other nodes down from root node
        if (level == 0)
            me.CalculateChildPositions();

        return me;
    }
}

public partial class Node
{
    /*
    * Now that the node tree is a structural copy of the expression tree, we can start calculating positions for our childs
    */
    public void CalculateChildPositions(bool isRoot = true)
    {
        if (isRoot)
        {
            this.from = 0;
            this.to = requiredHeight;
        }

        this.outputHeight = this.from + (this.to - this.from) / 2 - Node.BASE_SIZE / 2;

        if (this.childs is not { })
            return;

        for (int i = 0; i < this.childs!.Count; i++)
        {
            var child = this.childs![i];

            if (i == 0)
                child.from = this.from;
            else
                child.from = this.childs![i - 1].to;

            child.to = child.from + child.requiredHeight;

            child.outputHeight = child.from + (child.to - child.from) / 2 - Node.BASE_SIZE / 2;

            child.CalculateChildPositions(false);
        }
    }

    public List<Node> AllChilds()
    {
        List<Node> _childs = [];
        if (this.childs != null) 
        {
            foreach (Node child in this.childs!)
            {
                Debug.WriteLine("Processing child: " + child.character);
                _childs!.Add(child);
                Debug.WriteLine("1 Childs is now: " + _childs.Count);
                List<Node> childChilds = child.AllChilds();
                Debug.WriteLine("2 Childs is now: " + _childs.Count);
                _childs.AddRange(childChilds);
                Debug.WriteLine("3 Childs is now: " + _childs.Count);
            }
        }
        Debug.WriteLine("Returning childs: " + _childs.Count);
        return _childs!;
    }
}

class Canvas
{
    public static Image DrawCircuitLinear(Node root)
    {
        // Amount of collumns this node and its childs cover times the base node width
        int width = (root.deepestNode + 1) * Node.BASE_SIZE;
        int height = root.requiredHeight;

        Image<Rgb24> background = Canvas.ColoredRect(new Size(width, height), Color.White);

        // We're drawing left to right, but node positions are right to left
        int posX = background.Width - Node.BASE_SIZE;
        int posY = root.outputHeight - root.from;

        using Image tile = Canvas.LoadTile(root);
        background.Mutate(context =>
        {
            context.DrawImage(tile, new Point(posX, posY), 1f);
        });

        List<Node> childs = root.AllChilds();
        background.Mutate(context => 
        {
            foreach(Node child in childs)
            {
                int childX = background.Width - (child.column + 1) * Node.BASE_SIZE;
                int childY = child.from;
                using Image tile = Canvas.LoadTile(root);
                context.DrawImage(tile, new Point(posX, posY), 1f);
            }
        });

        return background;
    }

    //TODO: Instead of recursive painting, use absolute painting by collecting all nodes in a list before.
    public static Image DrawCircuitRecursive(Node tree)
    {
        // Amount of collumns this node and its childs cover times the base node width
        int width = (tree.deepestNode - tree.column + 1) * Node.BASE_SIZE;
        int height = tree.requiredHeight;

        Image<Rgb24> background = Canvas.ColoredRect(new Size(width, height), Color.White);

        // We're drawing left to right, but node positions are right to left
        int posX = background.Width - Node.BASE_SIZE;
        int posY = tree.outputHeight - tree.from;

        

        using Image tile = Canvas.LoadTile(tree);
        //Console.WriteLine($"Painting self ({tile.Width} * {tile.Height}) at {posX}|{posY}.");
        //Console.WriteLine($"I'm tile {tree.character} at collumn {tree.column}.");
        //Console.WriteLine($"My measurements are {background.Width} * {background.Height}");
        background.Mutate(context =>
        {
            context.DrawImage(tile, new Point(posX, posY), 1f);
        });

        if (tree.childs is {})
        {
            foreach (var child in tree.childs)
            {
                int childPosX = background.Width - (child.deepestNode - tree.column + 1) * Node.BASE_SIZE;
                int childPosY = child.from - tree.from;

                //Console.WriteLine($"Painting child (collumn {child.column} and height {child.requiredHeight}) at {childPosX}|{childPosY}.");

                var childTile = Canvas.DrawCircuitRecursive(child);

                background.Mutate(context =>
                {
                    context.DrawImage(childTile, new Point(childPosX, childPosY), 1f);
                });

                childTile.Dispose();
            }
        }

        return background;
    }



    public static Image<Rgb24> LoadTile(Node node, string tilePath = "tiles")
    {
        byte[] color = new byte[3];
        new Random().NextBytes(color);
        return node switch
        {
            { character: '^', invert: true } => Image.Load<Rgb24>($"{tilePath}/NAND.png"),
            { character: '^' } => Image.Load<Rgb24>($"{tilePath}/AND.png"),
            { character: 'v', invert: true } => Image.Load<Rgb24>($"{tilePath}/NOR.png"),
            { character: 'v' } => Image.Load<Rgb24>($"{tilePath}/OR.png"),
            { character: '⊕', invert: true } => Image.Load<Rgb24>($"{tilePath}/XNOR.png"),
            { character: '⊕' } => Image.Load<Rgb24>($"{tilePath}/XOR.png"),
            _ => Canvas.ColoredRect(new(Node.BASE_SIZE), new Rgb24(color[0], color[1], color[2]))
        };
    }

    public static Image<Rgb24> ColoredRect(Size dimensions, Rgb24 color)
    {
        Image<Rgb24> image = new Image<Rgb24>(dimensions.Width, dimensions.Height);

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                    row[x] = color;
            }
        });

        return image;
    }
}

// --- Main method ---

class Program 
{
    static void Main() 
    {
        string input;
        Console.WriteLine("Enter DNF-expression or type 'quit' to quit.");
        while((input = Console.ReadLine() ?? "") != "quit")
        {
            try
            {
                var tokens = Lexer.Tokenize(input);
                var expr = Expression.Parse(tokens);
                var root = expr.ToNodeTree();

                Console.WriteLine("\nParsed:");
                Console.WriteLine(expr.ToString());

                using var img = Canvas.DrawCircuitLinear(root);
                var fileName = $"DNF-{expr.ToString().Replace('(', '[').Replace(')', ']')}.png";
                img.Save(fileName);
                Console.WriteLine($"Wrote image to '{fileName}' \n");
                
            } catch (Exception e) { Console.WriteLine(e.ToString()); }
            
            Console.WriteLine("Enter DNF-expression or type 'quit' to quit.");
        }   
    }
}

