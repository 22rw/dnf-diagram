using Dumpify;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
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

}

class Canvas
{
    public static (Image Image, List<Node> Atoms) DrawCircuitRecursive(Node tree)
    {
        // List for all child-atoms for linear connection to the input tiles later on
        List<Node> childAtoms = new List<Node>();

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
            int inputDistance = 8;
            int counter = 0;

            foreach (var child in tree.childs)
            {
                // Calc child position on the canvas, the have the child draw itself and place the returned img on our canvas

                int childPosX = background.Width - (child.deepestNode - tree.column + 1) * Node.BASE_SIZE;
                int childPosY = child.from - tree.from;

                var (childTile, _childAtoms) = Canvas.DrawCircuitRecursive(child);

                background.Mutate(context =>
                {
                    context.DrawImage(childTile, new Point(childPosX, childPosY), 1f);
                });

                childTile.Dispose();

                // Check if child either is atom, in that case add to childAtoms, or has child atoms itself, in that case add its child atoms to childAtoms.
                // A child node has to be either atom or a node that has child atoms itself, so we only have to check if its an atom.

                if (child.character != '^' && child.character != 'v')
                    childAtoms.Add(child);
                else
                    foreach (var _childAtom in _childAtoms)
                        childAtoms.Add(_childAtom);


                // Calc the childs output and our input position and draw a connecting line

                int inputHeight = inputDistance * counter;
                int centeredInputHeight = inputHeight - (inputDistance * (tree.childs.Count - 1)) / 2;
                int absInputHeight = (tree.requiredHeight / 2) + centeredInputHeight;

                int absOutputHeight = (child.requiredHeight / 2) + childPosY;

                background.Mutate(context =>
                {
                    GraphicsOptions options = new()
                    {
                        Antialias = false,
                        AntialiasSubpixelDepth = 32
                    };
                    context.DrawLine(
                        new DrawingOptions() { GraphicsOptions = options },
                        Color.Black,
                        4f,
                        new PointF(childPosX + childTile.Width - 1, absOutputHeight - 1 + .5f),
                        new PointF(posX + 12.5f, absInputHeight - 1)
                    );
                });

                counter += 1;
            }
        }

        // For each of our child atoms, calc relative position and 'connect' it to left canvas edge
        Console.WriteLine($"Iterating over {childAtoms.Count} child atoms.");
        foreach (var atom in childAtoms)
        {
            int atomPosX = background.Width - (atom.deepestNode - tree.column + 1) * Node.BASE_SIZE;
            int atomPosY = atom.from - tree.from;

            if (atomPosX > 0)
            {
                Console.WriteLine($"child atom {atom.character} is not aligned with left edge.");
                for (int i = 0; i < atomPosX; i += Node.BASE_SIZE)
                    background.Mutate(context =>
                    {
                        context.DrawImage(Canvas.LoadTile(new()), new Point(i, atomPosY), 1f);
                    });
            }
        }
        Console.WriteLine("Finished iterating child atoms.");

        return (background, childAtoms);
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
            { character: { }, invert: true } => Image.Load<Rgb24>($"{tilePath}/NOT.png"),
            { character: { } } => Image.Load<Rgb24>($"{tilePath}/STRAIGHT.png"),
            _ => Image.Load<Rgb24>($"{tilePath}/STRAIGHT.png")
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

                var (image, _) = Canvas.DrawCircuitRecursive(root);
                var fileName = $"DNF-{expr.ToString().Replace('(', '[').Replace(')', ']')}.png";
                image.Save(fileName);
                Console.WriteLine($"Wrote image to '{fileName}' \n");
                image.Dispose();
                
            } catch (Exception e) { Console.WriteLine(e.ToString()); }
            
            Console.WriteLine("Enter DNF-expression or type 'quit' to quit.");
        }   
    }
}

