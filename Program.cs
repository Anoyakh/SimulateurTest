using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Globalization;
using System.Threading.Tasks;

public enum TileType
{
    Empty = 0,
    LowCover = 1,   // 50% de réduction des dégâts
    HighCover = 2   // 75% de réduction des dégâts
}

public struct Position
{
    public int X, Y;
    public Position(int x, int y) { X = x; Y = y; }

    public int ManhattanDistance(Position other)
        => Math.Abs(X - other.X) + Math.Abs(Y - other.Y);

    public static readonly Position[] OrthogonalDirections = {
        new Position( 1,  0),
        new Position(-1,  0),
        new Position( 0,  1),
        new Position( 0, -1),
    };
}

public class AgentData
{
    public int AgentId;
    public int PlayerId;
    public int ShootCooldown;
    public int OptimalRange;
    public int SoakingPower;
    public int SplashBombs;

    public Position Pos;
}

public class AgentState
{
    public int AgentId;
    public int PlayerId;
    public Position Pos;
    public int Cooldown;
    public int SplashBombs;
    public int Wetness;
    // Copie des paramètres initiaux nécessaires au tour
    public int OptimalRange;
    public int SoakingPower;

    public bool HunkeredThisTurn { get; set; } = false;
}

public class Tile
{
    public TileType Type;
}

// -----------------------------------------------------------------------
// Lecture et mise à jour du GameState
// -----------------------------------------------------------------------

public class GameState
{
    public int MyPlayerId;
    public List<AgentData> InitialAgents = new List<AgentData>();

    public int[] PointsByPlayer { get; } = new int[2];

    public int Width, Height, Turn;
    public Tile[,] Map;

    public List<AgentState> AllAgents = new List<AgentState>();
    public List<AgentState> MyAgents => AllAgents.Where(a => a.PlayerId == MyPlayerId).ToList();
    public List<AgentState> EnemyAgents => AllAgents.Where(a => a.PlayerId != MyPlayerId).ToList();

    public void ReadInitialization(TextReader reader)
    {
        Turn = 0;
        MyPlayerId = int.Parse(reader.ReadLine());
        int agentDataCount = int.Parse(reader.ReadLine());
        InitialAgents.Clear();
        for (int i = 0; i < agentDataCount; i++)
        {
            var parts = reader.ReadLine().Split();
            var data = new AgentData
            {
                AgentId       = int.Parse(parts[0]),
                PlayerId      = int.Parse(parts[1]),
                ShootCooldown = int.Parse(parts[2]),
                OptimalRange  = int.Parse(parts[3]),
                SoakingPower  = int.Parse(parts[4]),
                SplashBombs   = int.Parse(parts[5])
            };
            InitialAgents.Add(data);
        }

        var wh = reader.ReadLine().Split();
        Width  = int.Parse(wh[0]);
        Height = int.Parse(wh[1]);
        Map = new Tile[Width, Height];

        for (int i = 0; i < Height; i++)
        {
            var t = Console.ReadLine().Split();
            for(int j = 0; j < Width; j++ )
            {
                int x = int.Parse(t[j+j+j]);
                int y = int.Parse(t[j+j+j+1]);
                TileType type = (TileType)int.Parse(t[j+j+j+2]);
                Map[x, y] = new Tile { Type = type };
            }
        }
    }

    /// <summary>
    /// Mise à jour de l'état des agents pour le tour 0 (premier tour joué).
    /// </summary>
    public void UpdateTurn(TextReader reader)
    {
        Turn = 1;
        AllAgents.Clear();

        int agentCount = int.Parse(reader.ReadLine());
        for (int i = 0; i < agentCount; i++)
        {
            var parts = reader.ReadLine().Split();
            int agentId     = int.Parse(parts[0]);
            int x           = int.Parse(parts[1]);
            int y           = int.Parse(parts[2]);
            int cooldown    = int.Parse(parts[3]);
            int splashBombs = int.Parse(parts[4]);
            int wetness     = int.Parse(parts[5]);

            var init = InitialAgents.First(d => d.AgentId == agentId);
            AllAgents.Add(new AgentState
            {
                AgentId      = agentId,
                PlayerId     = init.PlayerId,
                Pos          = new Position(x, y),
                Cooldown     = cooldown,
                SplashBombs  = splashBombs,
                Wetness      = wetness,
                OptimalRange = init.OptimalRange,
                SoakingPower = init.SoakingPower
            });
        }

        // on lit le nombre de mes agents (inutile, on filtre avec PlayerId)
        int myAgentCount = int.Parse(reader.ReadLine());
    }

    /// <summary>
    /// Charge un GameState complet (init + premier tour) depuis un fichier texte.
    /// </summary>
    public static GameState LoadFromFile(string path)
    {
        using var sr = new StreamReader(path);
        var state = new GameState();
        state.ReadInitialization(sr);
        state.InitializeAgentsFromData();   // construit AllAgents à Turn=0
        state.UpdateTurn(sr);              // lit la 1ère boucle pour placer AllAgents + Turn=1
        return state;
    }

    /// <summary>
    /// Lecture de l'initialisation (appelée une seule fois).
    /// </summary>
    public void ReadInitialization()
    {
        Turn = 0;
        MyPlayerId = int.Parse(Console.ReadLine());
        int agentDataCount = int.Parse(Console.ReadLine());
        for (int i = 0; i < agentDataCount; i++)
        {
            var parts = Console.ReadLine().Split();
            var data = new AgentData
            {
                AgentId       = int.Parse(parts[0]),
                PlayerId      = int.Parse(parts[1]),
                ShootCooldown = int.Parse(parts[2]),
                OptimalRange  = int.Parse(parts[3]),
                SoakingPower  = int.Parse(parts[4]),
                SplashBombs   = int.Parse(parts[5])
            };
            InitialAgents.Add(data);
        }

        var wh = Console.ReadLine().Split();
        Width  = int.Parse(wh[0]);
        Height = int.Parse(wh[1]);
        Map = new Tile[Width, Height];
        for (int i = 0; i < Height; i++)
        {
            var t = Console.ReadLine().Split();
            for(int j = 0; j < Width; j++ )
            {
                int x = int.Parse(t[j+j+j]);
                int y = int.Parse(t[j+j+j+1]);
                TileType type = (TileType)int.Parse(t[j+j+j+2]);
                Map[x, y] = new Tile { Type = type };
            }
        }
    }

    public bool IsVictory(int playerId)
    {
        int other = 1 - playerId;

        // 1) Élimination totale
        bool enemyAlive = AllAgents
            .Any(a => a.PlayerId == other && a.Wetness < 100);
        if (!enemyAlive)
            return true;

        // 2) Avance ≥ 600 points
        if (PointsByPlayer[playerId] - PointsByPlayer[other]
            >= 600)
        {
            return true;
        }

        // 3) Fin de partie (100 tours) : si on a plus de points
        if (Turn >= 100
            && PointsByPlayer[playerId] > PointsByPlayer[other])
        {
            return true;
        }

        return false;
    }

    public int EvaluateWinnerByScore()
    {
        int p0 = PointsByPlayer[0];
        int p1 = PointsByPlayer[1];

        if (p0 > p1) return 0;
        if (p1 > p0) return 1;
        return -1;
    }

    public void PruneDeadAgents()
        => AllAgents.RemoveAll(a => a.Wetness >= 100);

    public GameState CloneForPlayer(int playerId)
    {
        var c = this.Clone();
        c.MyPlayerId = playerId;
        return c;
    }

    public void InitializeAgentsFromData()
    {
        Turn = 0;
        AllAgents.Clear();
        foreach (var d in InitialAgents)
        {
            AllAgents.Add(new AgentState {
                AgentId      = d.AgentId,
                PlayerId     = d.PlayerId,
                Pos          = d.Pos,
                Cooldown     = 0,
                SplashBombs  = d.SplashBombs,
                Wetness      = 0,
                OptimalRange = d.OptimalRange,
                SoakingPower = d.SoakingPower
            });
        }
    }

    public void UpdateTurn()
    {
        Turn++;
        AllAgents.Clear();
        int agentCount = int.Parse(Console.ReadLine());
        for (int i = 0; i < agentCount; i++)
        {
            var parts = Console.ReadLine().Split();
            int agentId     = int.Parse(parts[0]);
            int x           = int.Parse(parts[1]);
            int y           = int.Parse(parts[2]);
            int cooldown    = int.Parse(parts[3]);
            int splashBombs = int.Parse(parts[4]);
            int wetness     = int.Parse(parts[5]);

            // Retrouver les données initiales pour ce agent
            var init = InitialAgents.First(d => d.AgentId == agentId);

            AllAgents.Add(new AgentState
            {
                AgentId      = agentId,
                PlayerId     = init.PlayerId,
                Pos          = new Position(x, y),
                Cooldown     = cooldown,
                SplashBombs  = splashBombs,
                Wetness      = wetness,
                OptimalRange = init.OptimalRange,
                SoakingPower = init.SoakingPower
            });
        }

        // on lit le nombre de nos agents (inutile ici, on les filtre)
        int myAgentCount = int.Parse(Console.ReadLine());
    }

    public GameState Clone()
    {
        var clone = new GameState
        {
            MyPlayerId = this.MyPlayerId,
            Width      = this.Width,
            Height     = this.Height,
            Turn       = this.Turn
        };

        // 1) Cloner les données initiales (immutable, on peut partager si on veut)
        clone.InitialAgents = this.InitialAgents
            .Select(d => new AgentData
            {
                AgentId       = d.AgentId,
                PlayerId      = d.PlayerId,
                ShootCooldown = d.ShootCooldown,
                OptimalRange  = d.OptimalRange,
                SoakingPower  = d.SoakingPower,
                SplashBombs   = d.SplashBombs
            })
            .ToList();

        // 2) Cloner la carte
        clone.Map = new Tile[this.Width, this.Height];
        for (int x = 0; x < this.Width; x++)
        {
            for (int y = 0; y < this.Height; y++)
            {
                // TileType est un enum, on peut simplement recopier
                clone.Map[x, y] = new Tile { Type = this.Map[x, y].Type };
            }
        }

        // 3) Cloner l'état des agents
        clone.AllAgents = this.AllAgents
            .Select(a => new AgentState
            {
                AgentId      = a.AgentId,
                PlayerId     = a.PlayerId,
                Pos          = new Position(a.Pos.X, a.Pos.Y),
                Cooldown     = a.Cooldown,
                SplashBombs  = a.SplashBombs,
                Wetness      = a.Wetness,
                OptimalRange = a.OptimalRange,
                SoakingPower = a.SoakingPower
            })
            .ToList();

        return clone;
    }

    public float CalcCoverProtection(Position targetPos, Position attackerPos)
    {
        float bestProtection = 0f;

        // vecteur de l'attaquant vers la cible
        int vX = targetPos.X - attackerPos.X;
        int vY = targetPos.Y - attackerPos.Y;

        foreach (var dir in Position.OrthogonalDirections)
        {
            // position de la case de couverture candidate
            int cx = targetPos.X + dir.X;
            int cy = targetPos.Y + dir.Y;

            // 1) doit être dans la grille
            if (cx < 0 || cx >= Width || cy < 0 || cy >= Height)
                continue;

            var tile = Map[cx, cy];
            // 2) doit être une couverture (pas une case vide)
            if (tile.Type == TileType.Empty)
                continue;

            // 3) s’assurer que la couverture se trouve "entre" l’attaquant et la cible
            //    on prend le vecteur (cible→couverture) = dir
            //    et le vecteur (attaque→cible) = (vX,vY)
            //    pour qu’elle soit face à l’attaquant : leur produit scalaire doit être négatif
            if (dir.X * vX + dir.Y * vY >= 0)
                continue;

            // 4) si l'attaquant est adjacent (en 8 directions) à cette case, on ignore la couverture
            int dxCover = Math.Abs(attackerPos.X - cx);
            int dyCover = Math.Abs(attackerPos.Y - cy);
            if (Math.Max(dxCover, dyCover) == 1)
                continue;

            // 5) on a une couverture valide : appliquer la réduction correspondante
            float protection = tile.Type == TileType.HighCover ? 0.75f : 0.50f;
            bestProtection = Math.Max(bestProtection, protection);
        }

        return bestProtection;
    }
}

// -----------------------------------------------------------------------
// Logique de décision
// -----------------------------------------------------------------------

public class ActionSeq
{
    /// <summary>
    /// Liste ordonnée des commandes à exécuter (ex. "MOVE x y", "SHOOT id", "HUNKER_DOWN", "THROW x y").
    /// </summary>
    public List<string> Cmds { get; }

    /// <summary>
    /// Position finale de l'agent après avoir exécuté MOVE (ou sa position initiale si aucun MOVE).
    /// </summary>
    public Position Dest { get; }

    public ActionSeq(List<string> cmds, Position dest)
    {
        Cmds = cmds;
        Dest = dest;
    }
}

public interface IDecisionMaker
{
    /// <summary>
    /// Initialise le decision maker (appelé une seule fois en début de partie).
    /// </summary>
    /// <param name="initialState">État initial (map, agents, etc.).</param>
    /// <param name="myPlayerId">Votre identifiant joueur (0 ou 1).</param>
    void Initialize(GameState initialState, int myPlayerId);

    /// <summary>
    /// Génère les commandes pour tous les agents ce tour-ci.
    /// </summary>
    /// <param name="state">État complet du jeu pour ce tour.</param>
    /// <param name="timeLimitMs">Temps maximum alloué pour cette décision.</param>
    /// <param name="topX">Paramètre de beam‑search ou de permu (taille du topX).</param>
    /// <returns>
    /// Liste de chaînes “agentId;ACTION1;ACTION2;…”, dans l’ordre arbitraire des agents.
    /// </returns>
    IList<string> Decide(GameState state, int timeLimitMs, int topX, bool PassTurn);
}

public class MakeDecisionPermuted : IDecisionMaker
{
    private GameState _initialState;
    private List<AgentState> _myAgents;
    private List<AgentState> _enemyAgents;
    private readonly TuningOptions Tuning;
    private readonly Random _rng = new Random();

    // Actions et évaluations individuelles en cache
    private readonly Dictionary<int, List<ActionSeq>> _myActions    = new();
    private readonly Dictionary<int, List<float>>     _myEvalCache  = new();
    private readonly Dictionary<int, List<ActionSeq>> _enActions    = new();
    private readonly Dictionary<int, List<float>>     _enEvalCache  = new();

    private readonly int HazardousStart = 2;

    public MakeDecisionPermuted(GameState state, TuningOptions tuning)
    {
        _initialState = state;
        Tuning = tuning;
    }

    public MakeDecisionPermuted(TuningOptions tuning)
    {
        Tuning = tuning;
    }

    private TerritoryHelper _territoryHelper;


    /// <summary>
    /// Appelé une seule fois en début de partie.
    /// Stocke l'état initial et prépare le TerritoryHelper.
    /// </summary>
    public void Initialize(GameState initialState, int myPlayerId)
    {
        // Clone pour ne pas modifier l'original
        _initialState = initialState.Clone();
        _initialState.MyPlayerId = myPlayerId;

        // Pré‑préparer le Territoire
        _territoryHelper = new TerritoryHelper(_initialState);
        _territoryHelper.PrecomputeEnemyDistances();
    }

    /// <summary>
    /// Appelé chaque tour par le simulateur.
    /// Mets à jour l'état courant, recalcule les distances et lance DecideFullEval.
    /// </summary>
    public IList<string> Decide(GameState state, int timeLimitMs, int topX, bool PassTurn = false)
    {
        _initialState = state.Clone();
        if (state.Turn <= HazardousStart && !PassTurn)
        {
            var rnd = new Random();
            var occupied = new HashSet<Position>(state.AllAgents.Select(a => a.Pos));
            var cmds = new List<string>();

            foreach (var me in state.MyAgents)
            {
                // génère toutes ses séquences d'actions possibles sur ce tour
                RecomputePerTurnCaches();
                var seqs = GenerateActionSeqs(me, occupied, isMyTurn: true);
                // en tire une au hasard
                var choice = seqs[rnd.Next(seqs.Count)];
                cmds.Add($"{me.AgentId};{string.Join(";", choice.Cmds)}");

                // mettre à jour l'occupation pour le MOVE
                // (on suppose que choice.Dest est la destination après MOVE ou la position initiale)
                occupied.Remove(me.Pos);
                occupied.Add(choice.Dest);
            }

            return cmds;
        }

        else if (state.Turn <= HazardousStart && PassTurn)
        {
            var cmds = new List<string>();
            foreach (var me in state.MyAgents)
            {
                cmds.Add($"{me.AgentId};HUNKER_DOWN");
            }

            return cmds;
        }
        
        
        // 1) Mettre à jour l'état interne
        
        // MyPlayerId est déjà dans state, mais on s'assure
        // (utile si vous clonez d'une autre manière)
        // _initialState.MyPlayerId = state.MyPlayerId;

        // 2) Mettre à jour le helper Voronoi
        _territoryHelper = new TerritoryHelper(_initialState);
        _territoryHelper.PrecomputeEnemyDistances();

        // 3) Appeler votre DecideFullEval existant
        return DecideFullEval(timeLimitMs, topX, true);
    }

    static readonly Position[] ThrowOffsets = ComputeThrowOffsets();

    private static Position[] ComputeThrowOffsets()
    {
        var list = new List<Position>();
        for (int dx = -4; dx <= 4; dx++)
        for (int dy = -4; dy <= 4; dy++)
        {
            if (Math.Abs(dx) + Math.Abs(dy) > 4)      continue; // hors portée
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) <= 1) continue; // splash immédiat
            list.Add(new Position(dx, dy));
        }
        return list.ToArray();
    }

    private void RecomputePerTurnCaches()
    {
        _myActions.Clear();
        _myEvalCache.Clear();
        _enActions.Clear();
        _enEvalCache.Clear();

        _myAgents    = _initialState.MyAgents;
        _enemyAgents = _initialState.EnemyAgents;

        var occupied = new HashSet<Position>(_initialState.AllAgents.Select(a => a.Pos));

        // Mes agents
        foreach (var me in _myAgents)
        {
            var seqs = GenerateActionSeqs(me, occupied);
            _myActions[me.AgentId]   = seqs;
            _myEvalCache[me.AgentId] = seqs
                .Select(s => EvaluateIndividual(me, s, _initialState))
                .ToList();
        }
    }

    private HashSet<Position> EstimateEnemyBombZones()
    {
        var dangerZones = new HashSet<Position>();
        // Pour chaque ennemi capable de THROW
        foreach (var e in _enemyAgents.Where(e => e.SplashBombs > 0))
        {
            // On part de sa position et des adjacentes libres
            var origins = new List<Position> { e.Pos };
            foreach (var d in Position.OrthogonalDirections)
            {
                var p = new Position(e.Pos.X + d.X, e.Pos.Y + d.Y);
                if (p.X < 0 || p.X >= _initialState.Width ||
                    p.Y < 0 || p.Y >= _initialState.Height) continue;
                if (_initialState.Map[p.X, p.Y].Type != TileType.Empty) continue;
                origins.Add(p);
            }

            // Pour chaque origine, on étend de ΔX,ΔY tel que |ΔX|+|ΔY|≤4
            foreach (var o in origins)
            {
                for (int dx = -4; dx <= 4; dx++)
                {
                    int maxDy = 4 - Math.Abs(dx);
                    int ox = o.X + dx;
                    if (ox < 0 || ox >= _initialState.Width) continue;
                    for (int dy = -maxDy; dy <= maxDy; dy++)
                    {
                        int oy = o.Y + dy;
                        if (oy < 0 || oy >= _initialState.Height) continue;
                        // Zone d’effet Chebyshev ≤1 autour de (ox,oy)
                        // On ajoute directement les 9 positions sans boucle
                        dangerZones.Add(new Position(ox,   oy));
                        if (ox-1 >=0) dangerZones.Add(new Position(ox-1, oy));
                        if (ox+1 <_initialState.Width) dangerZones.Add(new Position(ox+1, oy));
                        if (oy-1 >=0) dangerZones.Add(new Position(ox,   oy-1));
                        if (oy+1 <_initialState.Height) dangerZones.Add(new Position(ox,   oy+1));
                        if (ox-1>=0 && oy-1>=0) dangerZones.Add(new Position(ox-1,oy-1));
                        if (ox-1>=0 && oy+1<_initialState.Height) dangerZones.Add(new Position(ox-1,oy+1));
                        if (ox+1<_initialState.Width && oy-1>=0) dangerZones.Add(new Position(ox+1,oy-1));
                        if (ox+1<_initialState.Width && oy+1<_initialState.Height) dangerZones.Add(new Position(ox+1,oy+1));
                    }
                }
            }
        }
        return dangerZones;
    }

    private List<HashSet<Position>> EstimateEnemyExplosionZones()
    {
        var throwTargets = new HashSet<Position>();
        int w = _initialState.Width, h = _initialState.Height;

        // 1) Collecte de toutes les cibles possibles
        foreach (var e in _enemyAgents.Where(e => e.SplashBombs > 0))
        {
            var origins = new List<Position> { e.Pos };
            foreach (var d in Position.OrthogonalDirections)
            {
                var p = new Position(e.Pos.X + d.X, e.Pos.Y + d.Y);
                if (p.X < 0 || p.X >= w || p.Y < 0 || p.Y >= h) continue;
                if (_initialState.Map[p.X, p.Y].Type != TileType.Empty) continue;
                origins.Add(p);
            }

            foreach (var o in origins)
            {
                for (int dx = -4; dx <= 4; dx++)
                {
                    int maxDy = 4 - Math.Abs(dx);
                    int ox = o.X + dx;
                    if (ox < 0 || ox >= w) continue;
                    for (int dy = -maxDy; dy <= maxDy; dy++)
                    {
                        int oy = o.Y + dy;
                        if (oy < 0 || oy >= h) continue;
                        throwTargets.Add(new Position(ox, oy));
                    }
                }
            }
        }

        // 2) Construction d’une zone par cible unique
        var zones = new List<HashSet<Position>>(throwTargets.Count);
        foreach (var tgt in throwTargets)
        {
            var zone = new HashSet<Position>();
            for (int ex = -1; ex <= 1; ex++)
                for (int ey = -1; ey <= 1; ey++)
                {
                    int zx = tgt.X + ex, zy = tgt.Y + ey;
                    if (zx < 0 || zx >= w || zy < 0 || zy >= h) continue;
                    zone.Add(new Position(zx, zy));
                }
            zones.Add(zone);
        }

        return zones;
    }

    private float EvaluateFullTurn( List<(AgentState agent, ActionSeq seq)> friendly, List<HashSet<Position>> explosionZones, HashSet<Position> dangerZones, List<Position> throwTargets)
    {
        //var swEval = Stopwatch.StartNew();
        // (0) Clone du GameState
        var sim = _initialState.Clone();

        // (1) Compter les "alive" initiaux
        int initialMyAlive    = sim.MyAgents   .Count(a => a.Wetness < 100);
        int initialTheirAlive = sim.EnemyAgents.Count(a => a.Wetness < 100);

        // (2) Calculer mes zones de bombes (hazard zones)
        var hunkeredAllies  = new HashSet<int>();
        var myBombZones = new HashSet<Position>();
        int W = sim.Width, H = sim.Height;
        var friendlySplashAdj = new bool[W, H];
        // (3) Appliquer mes MOVE dans sim
        foreach (var (me, seq) in friendly)
        {
            var last = seq.Cmds.Last();
            if (last.StartsWith("THROW"))
            {
                var parts = last.Split(' ');
                int tx = int.Parse(parts[1]), ty = int.Parse(parts[2]);
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        myBombZones.Add(new Position(tx + dx, ty + dy));
            }
            if (seq.Cmds.Contains("HUNKER_DOWN"))
            hunkeredAllies.Add(me.AgentId);
            var a = sim.AllAgents.First(x => x.AgentId == me.AgentId);
            var d = seq.Dest;
            a.Pos = d;
            for (int ex = -1; ex <= 1; ex++)
            for (int ey = -1; ey <= 1; ey++)
            {
                int sx = d.X + ex, sy = d.Y + ey;
                if (sx >= 0 && sx < W && sy >= 0 && sy < H)
                    friendlySplashAdj[sx, sy] = true;
            }
        }

        //Console.Error.WriteLine("Eval - BeforeGreedy :" + 1000 * (decimal)swEval.ElapsedTicks/(decimal)Stopwatch.Frequency + "ms");
        var occ = new HashSet<Position>(sim.AllAgents.Select(a => a.Pos));
        var hunkeredEnemies = new HashSet<int>();
        // (4) Greedy tour de l’adversaire : MOVE + COMBAT
        foreach (var en in sim.EnemyAgents.ToList())
        {
            // 4.2 Générer ses séquences
            var seqs = new List<ActionSeq>();

            // 1) MOVE possibles (cases adjacentes libres + rester)
            var moves = new List<Position> { en.Pos };
            foreach (var d in Position.OrthogonalDirections)
            {
                var p = new Position(en.Pos.X + d.X, en.Pos.Y + d.Y);
                if (p.X < 0 || p.X >= _initialState.Width
                    || p.Y < 0 || p.Y >= _initialState.Height)
                    continue;
                if (_initialState.Map[p.X, p.Y].Type != TileType.Empty)
                    continue;
                if (occ.Contains(p))
                    continue;
                moves.Add(p);
            }

            bool cooldownOk = en.Cooldown == 0, canThrow = en.SplashBombs > 0;

            // 4) Move + Combat / Move + Hunker
            foreach (var mv in moves)
            {
                if (!mv.Equals(en.Pos))
                {
                    // Move puis Hunker
                    seqs.Add(new ActionSeq(new List<string> { $"MOVE {mv.X} {mv.Y}", "HUNKER_DOWN" }, mv));
                }
                else
                {
                    // Hunker seul
                    seqs.Add(new ActionSeq(new List<string> { "HUNKER_DOWN" }, en.Pos));
                }

                if (cooldownOk)
                    foreach (var e in _myAgents)
                    {
                        int d = mv.ManhattanDistance(e.Pos);
                        if (d > 0 && d <= en.OptimalRange * 2)
                            seqs.Add(new ActionSeq(new List<string> {
                                    $"MOVE {mv.X} {mv.Y}",
                                    $"SHOOT {e.AgentId}"
                                }, mv));
                    }

                if (canThrow)
                    foreach (var off in ThrowOffsets)  // static readonly Position[] pré‑calculé
                    {
                        int tx = mv.X + off.X, ty = mv.Y + off.Y;
                        if (tx < 0 || tx >= W || ty < 0 || ty >= H) continue;
                        // **filtre O(1)** : on ne tente le THROW que si friendlySplashAdj[tx,ty]
                        if (!friendlySplashAdj[tx, ty]) continue;

                        seqs.Add(new ActionSeq(
                            new List<string>{ $"MOVE {mv.X} {mv.Y}", $"THROW {tx} {ty}" },
                            mv
                        ));
                    }
            }
        
            // 4.3 Les évaluer en lui passant mes bombes à éviter + zones de danger existantes
            var evals = seqs.Select(s =>
                    EvaluateIndividual(en, s, sim, false, myBombZones)
                ).ToList();

            // 4.4 Choisir la meilleure et l’appliquer
            int bestIdx = evals.IndexOf(evals.Max());
            var bestSeq = seqs[bestIdx];

            if (bestSeq.Cmds.Contains("HUNKER_DOWN"))
                hunkeredEnemies.Add(en.AgentId);

            // MOVE
            var agentSim = sim.AllAgents.First(x => x.AgentId == en.AgentId);
            agentSim.Pos = bestSeq.Dest;
            // COMBAT
            ApplyCombatWithHunker(sim, en.AgentId, bestSeq.Cmds.Last(), hunkeredEnemies);
        }

        //Console.Error.WriteLine("Eval - AfterGreedy :" + 1000 * (decimal)swEval.ElapsedTicks/(decimal)Stopwatch.Frequency + "ms");

        // (5) Appliquer mes COMBATS
        foreach (var (me, seq) in friendly)
        {
            ApplyCombatWithHunker(sim, me.AgentId, seq.Cmds.Last(), hunkeredAllies);
        }

        //Console.Error.WriteLine("Eval - BeforeVoronoi :" + 1000 * (decimal)swEval.ElapsedTicks/Stopwatch.Frequency + "ms");

        // (6) Calcul du territory diff optimisé
        var myFinals = friendly
            .Select(p => (agent: p.agent, dest: p.seq.Dest))
            .ToList();
        int territoryDiff = _territoryHelper
            .ComputeTerritoryDiffOptimized(myFinals);

        //Console.Error.WriteLine("Eval - AfterVoronoi :" + 1000 * (decimal)swEval.ElapsedTicks/(decimal)Stopwatch.Frequency + "ms");

        // (7) Comptage de kills nets et santé
        int finalMyAlive    = sim.MyAgents   .Count(a => a.Wetness < 100);
        int finalTheirAlive = sim.EnemyAgents.Count(a => a.Wetness < 100);
        int killsDiff = (initialTheirAlive - finalTheirAlive)
                    - (initialMyAlive    - finalMyAlive);

        int myHealth    = sim.MyAgents
            .Where(a => a.Wetness < 100).Sum(a => 100 - a.Wetness);
        int theirHealth = sim.EnemyAgents
            .Where(a => a.Wetness < 100).Sum(a => 100 - a.Wetness);
        int maxWetness = sim.EnemyAgents.Max(e => Math.Min(100, e.Wetness));

        // (8) Score final
        float score = territoryDiff * Tuning.TerritoryWeight
                    + killsDiff       * Tuning.KillWeight
                    + (myHealth - theirHealth) * Tuning.HealthDifferenceWeight
                    + maxWetness * Tuning.MaxWetnessWeight;

        foreach (var target in throwTargets)
        {
            // compter mes agents dont la destination est dans la zone Chebyshev ≤1 de ce centre
            int count = friendly.Count(p =>
                Math.Max(
                    Math.Abs(p.seq.Dest.X - target.X),
                    Math.Abs(p.seq.Dest.Y - target.Y)
                ) <= 1
            );

            if (count >= 2)
                score -= Tuning.MultiHitPenalty * count;
        }
        foreach (var (me, seq) in friendly)
        {
            if (dangerZones.Contains(seq.Dest))
                score -= Tuning.DangerZonePenalty;
        }

        int totalCooldown = sim.MyAgents.Sum(a => a.Cooldown);
        // Punir un tir à vide
        score -= totalCooldown * Tuning.CooldownPenaltyFactor;

        if(myHealth <= 0) score -= Tuning.DeathPenalty;
        if(theirHealth <= 0) score += Tuning.DeathPenalty;

        //Console.Error.WriteLine("Eval - End :" + 1000 * (decimal)swEval.ElapsedTicks/(decimal)Stopwatch.Frequency + "ms");

        return score;
    }

    private List<Position> EstimateEnemyThrowTargets()
    {
        var targets = new HashSet<Position>();
        int w = _initialState.Width, h = _initialState.Height;

        foreach (var e in _enemyAgents.Where(e => e.SplashBombs > 0))
        {
            // origines possibles
            var origins = new List<Position> { e.Pos };
            foreach (var d in Position.OrthogonalDirections)
            {
                var p = new Position(e.Pos.X + d.X, e.Pos.Y + d.Y);
                if (p.X < 0 || p.X >= w || p.Y < 0 || p.Y >= h) continue;
                if (_initialState.Map[p.X, p.Y].Type != TileType.Empty) continue;
                origins.Add(p);
            }

            // cibles possibles pour chaque origine
            foreach (var o in origins)
            {
                for (int dx = -4; dx <= 4; dx++)
                {
                    int maxDy = 4 - Math.Abs(dx);
                    int ox = o.X + dx;
                    if (ox < 0 || ox >= w) continue;
                    for (int dy = -maxDy; dy <= maxDy; dy++)
                    {
                        int oy = o.Y + dy;
                        if (oy < 0 || oy >= h) continue;
                        targets.Add(new Position(ox, oy));
                    }
                }
            }
        }

        return targets.ToList();
    } 

    // Génère les 4 schémas d’actions pour un agent donné
    private List<ActionSeq> GenerateActionSeqs(AgentState me, HashSet<Position> occupied, bool isMyTurn = true)
    {
        var seqs = new List<ActionSeq>();

        // 1) MOVE possibles (cases adjacentes libres + rester)
        var moves = new List<Position> { me.Pos };
        foreach (var d in Position.OrthogonalDirections)
        {
            var p = new Position(me.Pos.X + d.X, me.Pos.Y + d.Y);
            if (p.X < 0 || p.X >= _initialState.Width
                || p.Y < 0 || p.Y >= _initialState.Height) 
                continue;
            if (_initialState.Map[p.X, p.Y].Type != TileType.Empty) 
                continue;
            if (occupied.Contains(p)) 
                continue;
            moves.Add(p);
        }

        bool cooldownOk = me.Cooldown == 0, canThrow = me.SplashBombs > 0;

        // 4) Move + Combat / Move + Hunker
        foreach (var mv in moves)
        {
            if (!mv.Equals(me.Pos))
            {
                // Move puis Hunker
                seqs.Add(new ActionSeq (new List<string> { $"MOVE {mv.X} {mv.Y}", "HUNKER_DOWN" },mv));
            }
            else
            {
                // Hunker seul
                seqs.Add(new ActionSeq (new List<string> { "HUNKER_DOWN" },me.Pos));
            }

            if (cooldownOk)
                foreach (var e in isMyTurn ? _enemyAgents : _myAgents)
                {
                    int d = mv.ManhattanDistance(e.Pos);
                    if (d > 0 && d <= me.OptimalRange * 2 + 1)
                        seqs.Add(new ActionSeq (new List<string> {
                                $"MOVE {mv.X} {mv.Y}",
                                $"SHOOT {e.AgentId}"
                            }, mv));
                }

            if (canThrow)
                for (int dx = -4; dx <= 4; dx++)
                for (int dy = -4; dy <= 4; dy++)
                {
                    if (Math.Abs(dx) + Math.Abs(dy) > 4) continue;
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) <= 1)
                        continue;
                    var t = new Position(mv.X + dx, mv.Y + dy);
                    if (t.X < 0 || t.X >= _initialState.Width
                        || t.Y < 0 || t.Y >= _initialState.Height) continue;
                    
                    if ( isMyTurn && (!_enemyAgents.Any(ea =>
                            Math.Max(Math.Abs(ea.Pos.X - t.X),
                                    Math.Abs(ea.Pos.Y - t.Y)) <= 1)) || _myAgents.Any(ea =>
                            Math.Max(Math.Abs(ea.Pos.X - t.X),
                                    Math.Abs(ea.Pos.Y - t.Y)) <= 1))
                        continue;
                    else if( !isMyTurn && !_myAgents.Any(ea =>
                            Math.Max(Math.Abs(ea.Pos.X - t.X),
                                    Math.Abs(ea.Pos.Y - t.Y)) <= 1))
                        continue;
                    
                    seqs.Add(new ActionSeq (new List<string> {
                            $"MOVE {mv.X} {mv.Y}",
                            $"THROW {t.X} {t.Y}"
                        }, mv));
                }
        }

        return seqs;
    }

    private float EvaluateIndividual(AgentState me, ActionSeq seq, GameState state, bool isMyTurn = true, HashSet<Position> hazardZones = null)
    {
        float sc = 0f;
        int W = state.Width, H = state.Height;
        int turn = state.Turn;

        // 1) Pré‑extract agents et compteurs
        var allAgents  = state.AllAgents;
        var enemies    = isMyTurn ? state.EnemyAgents : state.MyAgents;
        var friends    = isMyTurn ? state.MyAgents      : state.EnemyAgents;
        int nEn = enemies.Count;
        int nFr = friends.Count;

        // 2) Détecter MOVE vs action unique vs MOVE+action
        string moveCmd = null, actCmd = null;
        if (seq.Cmds.Count == 1)
        {
            // soit MOVE seul, soit HUNKER/SHOOT/THROW seul
            string c0 = seq.Cmds[0];
            if (c0[0] == 'M') moveCmd = c0;
            else              actCmd  = c0;
        }
        else if (seq.Cmds.Count >= 2)
        {
            moveCmd = seq.Cmds[0];
            actCmd  = seq.Cmds[1];
        }

        // 3) Position de tir et finale
        Position combatPos, finalPos;
        if (moveCmd != null)
        {
            combatPos = finalPos = seq.Dest;
        }
        else
        {
            combatPos = finalPos = me.Pos;
            if (turn < 4) sc -= Tuning.EarlyGamePenalty;
        }

        bool hunker = (actCmd != null && actCmd[0] == 'H');

        // 4) Combat
        if (actCmd != null)
        {
            if (actCmd[0] == 'S')  // SHOOT xxx
            {
                // parse targetId
                int targetId = int.Parse(actCmd.Substring(6));
                // trouver l'agent
                AgentState targ = null;
                for (int i = 0; i < allAgents.Count; i++)
                    if (allAgents[i].AgentId == targetId)
                    { targ = allAgents[i]; break; }

                // dégâts
                int dx = Math.Abs(combatPos.X - targ.Pos.X);
                int dy = Math.Abs(combatPos.Y - targ.Pos.Y);
                int d  = dx + dy;
                float raw = me.SoakingPower * (d <= me.OptimalRange
                                ? 1f
                                : (d == 2 * me.OptimalRange ? 0.3f : 0.5f));
                bool hunking = targ.Cooldown > 0 && targ.SplashBombs == 0;
                float prot = state.CalcCoverProtection(targ.Pos, combatPos) + (hunking ? 0.25f : 0);         
                float dmg  = raw * (1f - prot);
                sc += dmg;
                if (prot < 0.76f)
                    sc += (targ.Wetness + dmg) * 0.1f;
                if(prot == 1) sc -= Tuning.WastedShootPenalty;
            }
            else if (actCmd[0] == 'T')  // THROW x y
            {
                sc -= Tuning.ThrowWastePenalty;
                // parse once
                int sp = actCmd.IndexOf(' ');
                int tx = int.Parse(actCmd.Substring(sp + 1,
                                actCmd.IndexOf(' ', sp + 1) - (sp + 1)));
                int ty = int.Parse(actCmd.Substring(actCmd.LastIndexOf(' ') + 1));

                if (isMyTurn)
                {
                    // centre
                    for (int i = 0; i < nEn; i++)
                    {
                        var e = enemies[i];
                        if (e.Pos.X == tx && e.Pos.Y == ty)
                        {
                            sc += Tuning.ThrowCenterHitBonus;
                            break;
                        }
                    }
                    // zone Chebyshev ≤1
                    for (int cdx = -1; cdx <= 1; cdx++)
                    for (int cdy = -1; cdy <= 1; cdy++)
                    {
                        int cx = tx + cdx, cy = ty + cdy;
                        if (cx < 0 || cx >= W || cy < 0 || cy >= H) continue;
                        if (state.Map[cx, cy].Type != TileType.Empty) continue;

                        // y a‑t‑il un ennemi sur (cx,cy)?
                        bool hit = false;
                        for (int i = 0; i < nEn; i++)
                        {
                            var e = enemies[i];
                            if (e.Pos.X == cx && e.Pos.Y == cy)
                            {
                                sc += Tuning.ThrowAdjacentHitBonus;
                                hit = true;
                                break;
                            }
                        }
                        if (!hit)
                        {
                            // sinon, chaque ennemi adjacent orthogonalement
                            for (int i = 0; i < nEn; i++)
                            {
                                var e = enemies[i];
                                int md = Math.Abs(e.Pos.X - cx) + Math.Abs(e.Pos.Y - cy);
                                if (md == 1) sc += Tuning.ThrowNearEnemyBonus;
                            }
                        }
                    }
                }
                else
                {
                    // si je suis l'adversaire, malus alliés touchés
                    int count = 0;
                    for (int i = 0; i < nEn; i++)
                    {
                        var a = enemies[i];
                        int mdx = Math.Abs(a.Pos.X - tx),
                            mdy = Math.Abs(a.Pos.Y - ty);
                        if (Math.Max(mdx, mdy) <= 1) count++;
                    }
                    sc += count * 30f;
                }

                // pénalité alliés (toujours)
                int hitAllies = 0;
                for (int i = 0; i < nFr; i++)
                {
                    var a = friends[i];
                    int mdx = Math.Abs(a.Pos.X - tx),
                        mdy = Math.Abs(a.Pos.Y - ty);
                    if (Math.Max(mdx, mdy) <= 1) hitAllies++;
                }
                sc -= hitAllies * 30f;
            }
        }

        // 5) Couverture
        float cov = 0f;

        for (int i = 0; i < nEn; i++)
        {
            var e = enemies[i];
            if (e.Cooldown == 0)
            {
                int md = Math.Abs(finalPos.X - e.Pos.X)
                    + Math.Abs(finalPos.Y - e.Pos.Y);
                if (md <= e.OptimalRange)
                    cov += state.CalcCoverProtection(finalPos, e.Pos)
                        + (hunker ? 0.25f : 0f);
            }
        }
        
        sc += cov * Tuning.CoverBonus;

        // 6) Distance minimale
        int minD = int.MaxValue;

        for (int i = 0; i < nEn; i++)
        {
            var e = enemies[i];
            int md = Math.Abs(finalPos.X - e.Pos.X)
                + Math.Abs(finalPos.Y - e.Pos.Y);
            if (md < minD) minD = md;
        }
        
        sc -= minD * Tuning.ProximityBonus;

        // 7) Bonus hunker
        if (hunker) sc += 0.5f;

        // 8) Hazard zones
        if (!isMyTurn && hazardZones != null
            && hazardZones.Contains(finalPos))
        {
            sc -= Tuning.HazardZonePenalty;
        }

        return sc;
    }

    public List<string> DecideFullEval(int timeLimitMs, int topX, bool useSeconds = false)
    {
        var sw = Stopwatch.StartNew();

        RecomputePerTurnCaches();
        int n = _myAgents.Count;

        // 1) Construire d'abord les listes de toutes les actions et leurs évaluations
        var allActionLists = new List<List<ActionSeq>>(n);
        var allEvalLists   = new List<List<float>>(n);
        for (int i = 0; i < n; i++)
        {
            int id    = _myAgents[i].AgentId;
            allActionLists.Add(_myActions[id]);
            allEvalLists.  Add(_myEvalCache[id]);
        }

        // 2) On récupère le nombre d'actions disponibles par agent
        int[] actionCounts = allActionLists.Select(list => list.Count).ToArray();

        //Console.Error.WriteLine(
        //    "Initial action counts per agent: [" +
        //    string.Join(", ", actionCounts) +
        //    "]"
        //);

        // 3) On initialise K au maximum, puis on le décrémente jusqu'à ce que le produit
        //    des min(K, actionCounts[i]) soit ≤ topX.
        int K = actionCounts.Max();
        long prod;
        do
        {
            prod = 1;
            for (int i = 0; i < n; i++)
            {
                prod *= Math.Min(K, actionCounts[i]);
                if (prod > topX) break;  // pas la peine de continuer si déjà trop grand
            }
            if (prod > topX)
                K--;
        } while (K > 1 && prod > topX);

        //Console.Error.WriteLine($"Final K chosen: {K}");

        long finalPermCount = 1;
        for (int i = 0; i < n; i++)
            finalPermCount *= Math.Min(K, actionCounts[i]);

        //Console.Error.WriteLine($"Estimated total permutations: {finalPermCount}");

        // 4) On constitue enfin topActions en ne gardant pour chaque agent que
        //    les Math.Min(K, actionCounts[i]) meilleures actions.
        var topActions = new List<List<ActionSeq>>(n);
        for (int i = 0; i < n; i++)
        {
            var seqs  = allActionLists[i];
            var evals = allEvalLists  [i];
            int takeCount = Math.Min(K, seqs.Count);

            var best = seqs
                .Zip(evals, (s, v) => (seq: s, score: v))
                .OrderByDescending(x => x.score)
                .Take(takeCount)
                .Select(x => x.seq)
                .ToList();

            topActions.Add(best);
        }

        // 3) Danger & explosion zones une seule fois
        var dangerZones    = EstimateEnemyBombZones();
        var explosionZones = EstimateEnemyExplosionZones();
        var throwTargets = EstimateEnemyThrowTargets();

        // 4) Initialisation de la recherche permutée
        var pq   = new PriorityQueue<(int[] idx, float sum), float>();
        var seen = new HashSet<string>();
        int[] startIdx = Enumerable.Repeat(0, n).ToArray();
        float startSum = topActions.Select((list, i)
                        => _myEvalCache[_myAgents[i].AgentId][0]).Sum();
        pq.Enqueue((startIdx, startSum), -startSum);
        seen.Add(string.Join(",", startIdx));

        float bestScore = float.NegativeInfinity;
        int[] bestIdxs  = null;
        float secondBestScore  = float.NegativeInfinity;
        int[]  secondBestIdxs  = null;
        int nbIter = 0;
        int bestIter = 0;

        //Console.Error.WriteLine("Elapsed time before Loop : " + sw.ElapsedMilliseconds + "ms");

        // 5) Exploration
        while (pq.Count > 0 && sw.ElapsedMilliseconds < timeLimitMs)
        {
            var (cur, _) = pq.Dequeue();
            nbIter++;

            // construire la permutation courante
            var friendly = Enumerable.Range(0, n)
                .Select(i => (_myAgents[i], topActions[i][cur[i]]))
                .ToList();

            // évaluer FULL TURN (incluant greedy adverse)
            float sc = EvaluateFullTurn(friendly, explosionZones, dangerZones, throwTargets);
            if (sc > bestScore)
            {
                // l'ancien meilleur devient second meilleur
                secondBestScore = bestScore;
                secondBestIdxs  = bestIdxs;

                bestScore = sc;
                bestIdxs  = (int[])cur.Clone();
                bestIter  = nbIter;
            }
            else if (sc > secondBestScore)
            {
                secondBestScore = sc;
                secondBestIdxs  = (int[])cur.Clone();
            }

            // générer voisins
            for (int i = 0; i < n; i++)
            {
                if (cur[i] + 1 < topActions[i].Count)
                {
                    var nxt = (int[])cur.Clone();
                    nxt[i]++;
                    var key = string.Join(",", nxt);
                    if (seen.Add(key))
                    {
                        // on met à jour simplement la somme heuristique
                        float s2 = cur
                            .Select((ci, j) => _myEvalCache[_myAgents[j].AgentId][ci])
                            .Sum()
                            - _myEvalCache[_myAgents[i].AgentId][cur[i]]
                            + _myEvalCache[_myAgents[i].AgentId][nxt[i]];
                        pq.Enqueue((nxt, s2), -s2);
                    }
                }
            }
        }

        int[] chosenIdxs = bestIdxs;
        if (useSeconds && secondBestIdxs != null && _rng.NextDouble() < 0.4)
        {
            bestIdxs = secondBestIdxs;
        }

        //Console.Error.WriteLine($"DecideFullEval iters: {nbIter}, time: {sw.ElapsedMilliseconds}ms");
        //Console.Error.WriteLine("Best Eval was at Iteration " + bestIter);
        //Console.Error.WriteLine("Best Score : " + (double)bestScore);
        // 6) Construire les commandes à partir de bestIdxs
        if (bestIdxs != null)
        {
            var result = new List<string>();
            for (int i = 0; i < n; i++)
            {
                var me  = _myAgents[i];
                var seq = topActions[i][bestIdxs[i]];
                result.Add($"{me.AgentId};{string.Join(";", seq.Cmds)}");
            }
            return result;
        }

        // 7) Fallback si aucune permutation n'a été évaluée à temps
        return _myAgents
            .Select(a => $"{a.AgentId};HUNKER_DOWN")
            .ToList();
    }

    private void ApplyCombatWithHunker(GameState sim, int attackerId, string cmd, HashSet<int> hunkeredSet)
    {
        // On récupère l’agent attaquant
        var attacker = sim.AllAgents.First(a => a.AgentId == attackerId);

        if (cmd.StartsWith("SHOOT"))
        {
            int targetId = int.Parse(cmd.Substring(6));
            var target   = sim.AllAgents.First(a => a.AgentId == targetId);
            target.Cooldown = 5;

            // 1) distance
            int d = Math.Abs(attacker.Pos.X - target.Pos.X)
                + Math.Abs(attacker.Pos.Y - target.Pos.Y);

            // 2) raw damage
            float raw = attacker.SoakingPower
                    * (d <= attacker.OptimalRange ? 1f : 0.5f);

            // 3) cover protection
            float prot = sim.CalcCoverProtection(
                target.Pos, attacker.Pos
            );

            // 4) +25% si la cible s'est hunkered
            if (hunkeredSet.Contains(targetId))
                prot += 0.25f;

            // 5) appliquer wetness
            int dmg = (int)Math.Ceiling(raw * (1f - prot));
            target.Wetness += dmg;
        }
        else if (cmd.StartsWith("THROW"))
        {
            var parts = cmd.Split(' ');
            int tx = int.Parse(parts[1]), ty = int.Parse(parts[2]);

            // zone Chebyshev ≤1
            foreach (var targ in sim.AllAgents)
            {
                int ddx = Math.Abs(targ.Pos.X - tx),
                    ddy = Math.Abs(targ.Pos.Y - ty);
                if (Math.Max(ddx, ddy) <= 1)
                {
                    // pas de modification de prot : explosion ignore HUNKER_DOWN
                    targ.Wetness += 30;
                }
            }
        }
        // HUNKER_DOWN n'inflige pas de damage
    }
}

public class TerritoryHelper
{
    private readonly GameState _state;
    // liste des indices de cases vides (x + y*Width)
    private readonly List<int> _emptyCells = new();
    // coordonnées de chaque cellId → (x,y)
    private readonly int[] _cellX, _cellY;
    // tableau (taille = _emptyCells.Count) : distance minimale pondérée aux ennemis
    private int[] _enemyMinDist;

    public TerritoryHelper(GameState state)
    {
        _state = state;
        int W = state.Width, H = state.Height;
        int gridSize = W * H;
        _cellX = new int[gridSize];
        _cellY = new int[gridSize];

        // 1) construire _emptyCells et _cellX/_cellY
        int idx = 0;
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++, idx++)
            {
                _cellX[idx] = x;
                _cellY[idx] = y;
                if (state.Map[x, y].Type == TileType.Empty)
                    _emptyCells.Add(idx);
            }
        }
    }

    /// <summary>À appeler au début de chaque tour, une fois que state.AllAgents est à jour.</summary>
    public void PrecomputeEnemyDistances()
    {
        int n = _emptyCells.Count;
        _enemyMinDist = new int[n];

        var enemies = _state.EnemyAgents;
        for (int i = 0; i < n; i++)
        {
            int cellId = _emptyCells[i];
            int x = _cellX[cellId], y = _cellY[cellId];
            int best = int.MaxValue;

            foreach (var e in enemies)
            {
                int factor = e.Wetness >= 50 ? 2 : 1;
                int d = Math.Abs(e.Pos.X - x) + Math.Abs(e.Pos.Y - y);
                int wd = d * factor;
                if (wd < best) best = wd;
            }

            _enemyMinDist[i] = best;
        }
    }

    /// <summary>
    /// Renvoie territoryDiff = (#cases où bestMy < bestEn) – (#cas où bestEn < bestMy)
    /// </summary>
    public int ComputeTerritoryDiffOptimized(List<(AgentState agent, Position dest)> myFinalPositions)
    {
        int diff = 0;
        int n = _emptyCells.Count;

        for (int i = 0; i < n; i++)
        {
            int cellId = _emptyCells[i];
            int x = _cellX[cellId], y = _cellY[cellId];

            // 2) calculer bestMy en ne parcourant QUE mes agents
            int bestMy = int.MaxValue;
            foreach (var (me, dest) in myFinalPositions)
            {
                int factor = me.Wetness >= 50 ? 2 : 1;
                int d = Math.Abs(dest.X - x) + Math.Abs(dest.Y - y);
                int wd = d * factor;
                if (wd < bestMy) bestMy = wd;
            }

            int bestEn = _enemyMinDist[i];

            if (bestMy < bestEn)       diff++;
            else if (bestEn < bestMy)  diff--;
        }

        return diff;
    }
}

public interface ITuningOptions
{
    // Exemple de quelques propriétés :
    float DeathPenalty { get; set; }
    float TerritoryWeight { get; set; }
    float KillWeight { get; set; }
    float HealthDifferenceWeight { get; set; }
    float MaxWetnessWeight { get; set; }

    float MultiHitPenalty { get; set; }
    float DangerZonePenalty { get; set; }
    float HazardZonePenalty { get; set; }
    float EarlyGamePenalty { get; set; }
    float ProximityBonus { get; set; }
    float CoverBonus { get; set; }

    float CooldownPenaltyFactor { get; set; }
    float WastedShootPenalty { get; set; }

    float ThrowWastePenalty { get; set; }
    float ThrowCenterHitBonus { get; set; }
    float ThrowAdjacentHitBonus { get; set; }
    float ThrowNearEnemyBonus { get; set; }

    int TopPermutationsLimit { get; set; }
}

public class TuningOptions : ITuningOptions
{
    // Poids score final
    public float DeathPenalty             { get; set; } = 10000f;    // Mort mort mort
    public float TerritoryWeight          { get; set; } = 5f;    // x case contrôlées
    public float KillWeight               { get; set; } = 50f;   // x kill nettes
    public float HealthDifferenceWeight   { get; set; } = 0.1f;  // Δ somme (100–wetness)
    public float MaxWetnessWeight         { get; set; } = 0.15f; // wettest ennemi

    // Pénalités & bonus divers
    public float MultiHitPenalty          { get; set; } = 35f;   // 2+ agents dans même splash
    public float DangerZonePenalty        { get; set; } = 20f;   // land in enemy bomb zone
    public float HazardZonePenalty        { get; set; } = 5f;    // penalty for hazardZones (no‑shoot)
    public float EarlyGamePenalty         { get; set; } = 5f;    // turn<4 movement penalty
    public float ProximityBonus           { get; set; } = 0.3f;  // On encourage le rapprochement vers les cibles
    public float CoverBonus               { get; set; } = 3f;    // Bonus de couverture

    // Tuning cooldown & tir gaspillé
    public float CooldownPenaltyFactor    { get; set; } = 0.1f;  // punition somme des cooldowns
    public float WastedShootPenalty       { get; set; } = 10f;   // pour chaque shoot sans dégât

    // Heuristiques de throw (approximate)
    public float ThrowWastePenalty        { get; set; } = 12f;   // malus prime pour THROW
    public float ThrowCenterHitBonus      { get; set; } = 10f;   // touche cible
    public float ThrowAdjacentHitBonus    { get; set; } = 4f;    // touche case adj vide
    public float ThrowNearEnemyBonus      { get; set; } = 2f;    // enemy adjacent

    // Beam‑search / permu
    public int   TopPermutationsLimit     { get; set; } = 600;  // topX global
}


public interface IPlayer
{
    /// <summary>Appelé une seule fois après l'init du match.</summary>
    void Init(GameState state, int myPlayerId);

    /// <summary>
    /// Pour chaque tour, reçoit le state complet et renvoie
    /// une liste de chaînes "agentId;ACTION1;ACTION2;...".
    /// </summary>
    IList<string> Decide(GameState state, int myPlayerId);
}

public class CodinGameAdapter : IPlayer
{
    private readonly IDecisionMaker _decisionMaker;
    private readonly int           _timeLimitMs;
    private readonly int           _topX;
    private readonly bool _PassTurn;

    /// <summary>
    /// Crée un joueur qui utilisera ce decisionMaker avec ces paramètres de recherche.
    /// </summary>
    /// <param name="decisionMaker">Votre MakeDecisionPermuted (ou tout autre IDecisionMaker)</param>
    /// <param name="timeLimitMs">Temps (ms) autorisé par appel Decide</param>
    /// <param name="topX">Paramètre topX pour le beam‐search / permutations</param>
    public CodinGameAdapter(
        IDecisionMaker decisionMaker, bool passTurn,
        int timeLimitMs = 1000,
        int topX       = 600)
    {
        _decisionMaker = decisionMaker;
        _timeLimitMs   = timeLimitMs;
        _topX          = topX;
        _PassTurn       = passTurn;
    }

    /// <summary>
    /// Appelé une seule fois en début de match par le simulateur.
    /// </summary>
    public void Init(GameState state, int myPlayerId)
    {
        // Transmettre l'état initial et l'ID joueur à votre DecisionMaker
        _decisionMaker.Initialize(state, myPlayerId);
    }

    /// <summary>
    /// Appelé chaque tour ; renvoie la liste de commandes "agentId;ACTION;…"
    /// </summary>
    public IList<string> Decide(GameState state, int myPlayerId)
    {
        // Vous pouvez cloner ou passer directement state selon votre implémentation
        // Ici on suppose que votre DecisionMaker fait lui‐même le clone s'il en a besoin.
        return _decisionMaker.Decide(state, _timeLimitMs, _topX, _PassTurn);
    }
}

public static class GameStateGenerator
{
    /// <summary>
    /// Construit la map et les agents à partir d’une seed
    /// (prédictible, pour pouvoir rejouer la même partie).
    /// </summary>
    public static GameState FromSeed(int seed)
    {
        var rnd = new Random(seed);
        var state = new GameState();

        // 1) Dimensions aléatoires
        state.Width  = rnd.Next(12, 21);
        state.Height = rnd.Next(6, 11);

        int W = state.Width, H = state.Height;
        state.Map = new Tile[W, H];

        // 2) Grille vide partout
        for (int x = 0; x < W; x++)
        for (int y = 0; y < H; y++)
            state.Map[x, y] = new Tile { Type = TileType.Empty };

        // 3) Symétrie horizontale et bords vides
        // On ne touchera que les colonnes 1..W-2 et lignes 1..H/2-1
        const double coverColumnProb = 0.25;   // 25% de colonnes portent une paire de couvertures
        const double highCoverProb   = 0.33;   // parmi les couvertures, 33% serons hautes
        int halfRows = H / 2;                  // ex. H=9 → 4, H=10 →5

        for (int x = 1; x < W - 1; x++)
        {
            // avec probabilité coverColumnProb, on place
            // une couverture symétrique sur la moitié supérieure
            if (rnd.NextDouble() < coverColumnProb && halfRows > 1)
            {
                // choisir une ligne y dans [1 .. halfRows-1]
                int y = rnd.Next(1, halfRows);
                var type = rnd.NextDouble() < highCoverProb
                    ? TileType.HighCover
                    : TileType.LowCover;
                state.Map[x, y]             .Type = type;
                state.Map[x, H - 1 - y]     .Type = type;
            }
            // si H est impair, on peut aussi placer une couverture au centre
            if (H % 2 == 1 && rnd.NextDouble() < coverColumnProb / 2)
            {
                int yc = H / 2;
                var type = rnd.NextDouble() < highCoverProb
                    ? TileType.HighCover
                    : TileType.LowCover;
                state.Map[x, yc].Type = type;
            }
        }

        // 4) Placer les agents
        int perPlayer = rnd.Next(3, 6);            // entre 3 et 5
        int totalAgents = perPlayer * 2;
        state.InitialAgents.Clear();
        int nextId = 0;

        // Pour garantir que chaque joueur a la même config,
        // on génère d'abord 'perPlayer' positions uniques
        var positions = new List<Position>();
        while (positions.Count < perPlayer)
        {
            int x = rnd.Next(1, W - 1);
            int y = rnd.Next(1, H / 2);  // moitié supérieure
            var p = new Position(x, y);
            if (!positions.Contains(p) && state.Map[x, y].Type == TileType.Empty)
                positions.Add(p);
        }

        for (int i = 0; i < perPlayer; i++)
        {
            Position p1 = positions[i];
            Position p2 = new Position(p1.X, H - 1 - p1.Y);

            // Stats aléatoires identiques pour le couple
            int cd    = rnd.Next(1, 5);
            int orng  = rnd.Next(3, 6);
            int sp    = rnd.Next(10, 30);
            int bombs = rnd.Next(0, 3);

            // Agent du joueur 0
            state.InitialAgents.Add(new AgentData {
                AgentId       = nextId++,
                PlayerId      = 0,
                Pos           = p1,
                ShootCooldown = cd,
                OptimalRange  = orng,
                SoakingPower  = sp,
                SplashBombs   = bombs
            });
            // Agent symétrique du joueur 1
            state.InitialAgents.Add(new AgentData {
                AgentId       = nextId++,
                PlayerId      = 1,
                Pos           = p2,
                ShootCooldown = cd,
                OptimalRange  = orng,
                SoakingPower  = sp,
                SplashBombs   = bombs
            });
        }

        // 5) Finaliser AllAgents avec positions & stats
        state.InitializeAgentsFromData();

        return state;
    }
}

public class MatchSimulator
{

    /// <summary>
    /// Simule un match entre p1 et p2, retourne le résultat et les stats.
    /// </summary>
    public MatchResult RunMatch(IPlayer p1, IPlayer p2, int seed, int NbTurn = 100)
    {
        // Génération initiale
        var state = GameStateGenerator.FromSeed(seed);
        int pid1 = 0, pid2 = 1;

        // Init des joueurs
        p1.Init(state.CloneForPlayer(pid1), pid1);
        p2.Init(state.CloneForPlayer(pid2), pid2);

        // Boucle de tours
        for (int turn = 1; turn <= NbTurn; turn++)
        {
            state.Turn = turn;
            var state1 = state.CloneForPlayer(pid1);
            var state2 = state.CloneForPlayer(pid2);
            // 1) Solliciter les deux IA
            var task1 = Task.Run(() => p1.Decide(state1, pid1));
            var task2 = Task.Run(() => p2.Decide(state2, pid2));

            // 3) Attendre la fin des deux
            Task.WaitAll(task1, task2);

            // 4) Récupérer les résultats
            var cmds1 = task1.Result;
            var cmds2 = task2.Result;

            // 2) Parser + valider les commandes
            var actions1 = CommandParser.Parse(cmds1, state, pid1);
            var actions2 = CommandParser.Parse(cmds2, state, pid2);

            // 3) Appliquer MOVE (en 2 passes + collisions)
            CollisionResolver.ResolveMoves(state, actions1, actions2);

            // 4) HUNKER_DOWN
            var allActions = new Dictionary<int, ActionSeq>(actions1);
            foreach (var kv in actions2) allActions[kv.Key] = kv.Value;
            {
                CombatResolver.ApplyHunker(state, allActions);
                CombatResolver.ApplyShootAndThrow(state, allActions);
            }

            // 5) SHOOT / THROW
            //CombatResolver.ApplyShootAndThrow(state, actions1);
            //CombatResolver.ApplyShootAndThrow(state, actions2);

            foreach (var agent in state.AllAgents)
            {
                if (agent.Cooldown > 0)
                    agent.Cooldown--;
            }

            // 6) Retirer agents éliminés
            state.PruneDeadAgents();

            var helper = new TerritoryHelper(state);
            helper.PrecomputeEnemyDistances();

            // 1) Calculer la différence de territoire
            var myFinals = state.AllAgents.Where(a => a.PlayerId == pid1).Select(a => (agent: a, dest: a.Pos)).ToList();
            int territoryDiff = helper.ComputeTerritoryDiffOptimized(myFinals);

            // 2) Mettre à jour les points
            if (territoryDiff > 0)      state.PointsByPlayer[0] += territoryDiff;
            else if (territoryDiff < 0) state.PointsByPlayer[1] += -territoryDiff;

            bool p1Alive = state.AllAgents.Any(a => a.PlayerId == pid1 && a.Wetness < 100);
            bool p2Alive = state.AllAgents.Any(a => a.PlayerId == pid2 && a.Wetness < 100);
            if(!p1Alive && !p2Alive)
                return new MatchResult(-1, turn, state, VictoryReason.MatchNul);
            if (!p2Alive)
                return new MatchResult(pid1, turn, state, VictoryReason.Elimination);
            if (!p1Alive)
                return new MatchResult(pid2, turn, state, VictoryReason.Elimination);

            // victoire par avance de 600 pts ?
            int diff = state.PointsByPlayer[pid1] - state.PointsByPlayer[pid2];
            if (diff >= 600)
                return new MatchResult(pid1, turn, state, VictoryReason.PointLead);
            if (diff <= -600)
                return new MatchResult(pid2, turn, state, VictoryReason.PointLead);
        }


        int winner = state.PointsByPlayer[0] > state.PointsByPlayer[1] ? 0
                    : state.PointsByPlayer[1] > state.PointsByPlayer[0] ? 1
                    : -1;
        var reason = winner < 0 ? VictoryReason.MatchNul : VictoryReason.EndOfTurns;
        return new MatchResult(winner, 100, state, reason);
        
    }
}

class Tuner
{
  private readonly Random      _rnd = new Random();
  private readonly Func<IPlayer> _makeFixedPlayer;
    private readonly Func<TuningOptions, IPlayer> _makePlayer;
    private readonly int _matchesPerBatch;
    private readonly object _lock = new();

    public double BestWinRate { get; private set; } = 0.52;
    public TuningOptions CurrentBest { get; private set; }
    
    public Tuner(
        TuningOptions start,
        Func<IPlayer> makeFixedPlayer,
        Func<TuningOptions, IPlayer> makePlayer,
        int matchesPerBatch)
    {
        CurrentBest      = start;
        _makeFixedPlayer = makeFixedPlayer;
        _makePlayer      = makePlayer;
        _matchesPerBatch = matchesPerBatch;
    }
    
    public void Run(int maxIters)
    {
        var swTotal = Stopwatch.StartNew();
        
        for (int iter = 0; iter < maxIters; iter++)
        {
            // 1) Générer un voisin
            var candidate = Perturb(CurrentBest, sigma: 0.06);
            
            // 2) Évaluer son taux de victoire en batch, en parallèle
            int wins = 0;
            Parallel.For(0, _matchesPerBatch, new ParallelOptions {
                MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1)
            },
            // init per-thread
            () => 0,
            // body
            (i, loop, localWin) =>
            {
                int seed = iter * _matchesPerBatch + i;
                
                // chaque thread crée SA propre instance de playerA et playerB
                var playerA = _makeFixedPlayer();
                var playerB = _makePlayer(candidate);
                
                var sim    = new MatchSimulator();
                var result = sim.RunMatch(playerA, playerB, seed, 50);
                
                // on considère playerB comme idx=1
                if (result.WinnerId == 1) localWin++;
                return localWin;
            },
            // finalizer pour accumuler
            localWin => Interlocked.Add(ref wins, localWin)
            );
            
            double winRate = (double)wins / _matchesPerBatch;
            
            // 3) Si meilleur, on adopte et on log
            lock (_lock)
            {
                if (winRate > BestWinRate)
                {
                    BestWinRate = winRate;
                    CurrentBest = candidate;
                    LogImprovement(iter, candidate, winRate);
                }
            }
            
            Console.Error.WriteLine(
                $"Iter {iter,4}  WinRate {winRate:P1}  Best {BestWinRate:P1}  Elapsed {swTotal.Elapsed:hh\\:mm\\:ss}"
            );
        }
    }

  private TuningOptions Perturb(TuningOptions baseOpt, double sigma)
    {
        var next = new TuningOptions();
        var props = typeof(TuningOptions).GetProperties()
            .Where(p => p.CanRead && p.CanWrite);

        foreach (var prop in props)
        {
            var type = prop.PropertyType;
            var baseValue = prop.GetValue(baseOpt);

            if (type == typeof(float))
            {
                // on perturbe les floats
                float val = (float)baseValue;
                // tirage normal approximé N(val, sigma*val)
                double factor = 1 + NextGaussian() * sigma;
                factor = Math.Clamp(factor, 0.2, 2.0);
                prop.SetValue(next, val * (float)factor);
            }
            else if (type == typeof(int))
            {
                // on recopie les ints sans changement
                int val = (int)baseValue;
                prop.SetValue(next, val);
            }
            else
            {
                // si jamais d'autres types apparaissent, on les recopie aussi
                prop.SetValue(next, baseValue);
            }
        }

        return next;
    }

    private double EvaluateWinRate(TuningOptions opt)
    {
        int wins = 0;
        var sim = new MatchSimulator();

        for (int i = 0; i < _matchesPerBatch; i++)
        {
            int seed = 10000 + i;
            // recréer à chaque fois les deux joueurs pour éviter l'état partagé
            var playerA = _makeFixedPlayer();
            var playerB = _makePlayer(opt);

            var result = sim.RunMatch(playerA, playerB, seed, 50);
            if (result.WinnerId == 1)  // ici B est le joueur d'indice 1
                wins++;
        }

        return wins / (double)_matchesPerBatch;
    }

    private static readonly Dictionary<string,string> _tuningComments = new Dictionary<string,string>
    {
        // Poids score final
        ["DeathPenalty"]           = "Mort mort mort",
        ["TerritoryWeight"]        = "x case contrôlées",
        ["KillWeight"]             = "x kill nettes",
        ["HealthDifferenceWeight"] = "Δ somme (100–wetness)",
        ["MaxWetnessWeight"]       = "wettest ennemi",

        // Pénalités & bonus divers
        ["MultiHitPenalty"]   = "2+ agents dans même splash",
        ["DangerZonePenalty"] = "land in enemy bomb zone",
        ["HazardZonePenalty"] = "penalty for hazardZones (no‑shoot)",
        ["EarlyGamePenalty"]  = "turn<4 movement penalty",
        ["ProximityBonus"]    = "encourage rapprochement vers cibles",
        ["CoverBonus"]        = "Bonus de couverture",

        // Tuning cooldown & tir gaspillé
        ["CooldownPenaltyFactor"] = "punition somme des cooldowns",
        ["WastedShootPenalty"]    = "pour chaque shoot sans dégât",

        // Heuristiques de throw
        ["ThrowWastePenalty"]     = "malus prime pour THROW",
        ["ThrowCenterHitBonus"]   = "touche cible",
        ["ThrowAdjacentHitBonus"] = "touche case adj vide",
        ["ThrowNearEnemyBonus"]   = "ennemi adjacent"
    };

    private void LogImprovement(int iter, TuningOptions opt, double winRate)
    {
        // Assurer l'existence des dossiers
        Directory.CreateDirectory("logs");
        Directory.CreateDirectory("TuningOpt");

        // --- 1) CSV de suivi des métriques dans logs/tuning_log.csv ---
        const string csvFileName = "tuning_log.csv";
        string csvPath = Path.Combine("logs", csvFileName);

        var props = typeof(TuningOptions)
            .GetProperties()
            .Where(p => p.CanRead && p.CanWrite && p.Name != nameof(TuningOptions.TopPermutationsLimit))
            .ToArray();

        // Si nouveau fichier, écrire l'en‑tête
        if (!File.Exists(csvPath))
        {
            using var sw = new StreamWriter(csvPath, append: false);
            var headers = new[] { "Timestamp", "Iteration", "WinRate" }
                .Concat(props.Select(p => p.Name));
            sw.WriteLine(string.Join(",", headers));
        }

        // Ajouter la ligne de valeurs
        using (var sw = new StreamWriter(csvPath, append: true))
        {
            var values = new List<string>
            {
                DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                iter.ToString(),
                winRate.ToString("F3", CultureInfo.InvariantCulture)
            };
            values.AddRange(props.Select(p =>
            {
                var v = p.GetValue(opt);
                if (p.PropertyType == typeof(float))
                    return ((float)v).ToString("F4", CultureInfo.InvariantCulture);
                else
                    return v.ToString();
            }));
            sw.WriteLine(string.Join(",", values));
        }

        // --- 2) Génération du fichier C# prêt à copier/coller dans TuningOpt ---
        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        // remplacer les caractères invalides du winRate dans le nom de fichier
        string sanitizedRate = winRate.ToString("F3", CultureInfo.InvariantCulture).Replace('.', '_');
        string codeFileName = $"TuningOptions_BaseTuner_{sanitizedRate}_{timestamp}.cs";
        string codePath     = Path.Combine("TuningOpt", codeFileName);

        using var csw = new StreamWriter(codePath, append: false);

        csw.WriteLine($"public class TuningOptions_{sanitizedRate}_{timestamp} : ITuningOptions");
        csw.WriteLine("{");

        foreach (var p in props)
        {
            var name    = p.Name;
            var comment = _tuningComments.TryGetValue(name, out var c) ? c : "";
            var valObj  = p.GetValue(opt);
            var valStr  = ((float)valObj).ToString("F4", CultureInfo.InvariantCulture) + "f";

            csw.WriteLine($"    /// <summary>{comment}</summary>");
            csw.WriteLine($"    public float {name} {{ get; set; }} = {valStr};");
            csw.WriteLine();
        }

        csw.WriteLine("    // Beam‑search / permu (fixe, non tunable)");
        csw.WriteLine("    public int TopPermutationsLimit { get; set; } = 600;");
        csw.WriteLine("}");

        Console.WriteLine($"→ métriques ajoutées dans '{csvPath}' et snapshot code dans '{codePath}'");
    }

  private double NextGaussian()
  {
    // Box–Muller
    double u1 = 1.0 - _rnd.NextDouble();
    double u2 = 1.0 - _rnd.NextDouble();
    return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
  }
}

public class CoordinateDescentTuner
{
    private readonly Func<IPlayer>                _makeFixed;    // usine pour A
    private readonly Func<TuningOptions, IPlayer> _makeOpponent; // usine pour B
    private readonly int[]                        _budgets;
    private float                                 _stepFraction;
    private readonly int                          _maxSweeps;

    private readonly object _lock = new();

    public TuningOptions Best        { get; private set; }
    public double         BestWinRate { get; private set; }

    /// <summary>
    /// Au lieu de passer un IPlayer fixe, on passe une usine qui crée un NOUVEL IPlayer A à chaque appel.
    /// Même chose pour B, avec ses tuning.
    /// </summary>
    public CoordinateDescentTuner(TuningOptions start, Func<IPlayer> makeFixedPlayer, Func<TuningOptions,IPlayer> makeOpponent, int[] budgets = null, float stepFraction = 0.1f, int maxSweeps = 5)
    {
        Best           = Clone(start);
        BestWinRate    = 0.52;
        _makeFixed     = makeFixedPlayer;
        _makeOpponent  = makeOpponent;
        _budgets       = budgets ?? new[]{50,100,200};
        _stepFraction  = stepFraction;
        _maxSweeps     = maxSweeps;
    }

    public void Run()
    {
        var props = typeof(TuningOptions)
            .GetProperties(BindingFlags.Public|BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && p.PropertyType == typeof(float))
            .ToArray();

        int iter = 0;
        var sw = Stopwatch.StartNew();
        var BaseBest = Clone(Best);

        for (int sweep = 0; sweep < _maxSweeps; sweep++)
        {
            bool improved = false;

            foreach (var prop in props)
            {
                float baseVal = (float)prop.GetValue(BaseBest);
                float delta   = baseVal * _stepFraction;
                float current = (float)prop.GetValue(Best);

                // on génère trois candidats : –delta, +delta, et l’actuel
                var cands = new List<TuningOptions>{
                    Clone(Best),
                    Clone(Best),
                //    Clone(Best)
                };
                prop.SetValue(cands[0], current + delta);
                prop.SetValue(cands[1], current - delta);

                // successive halving sur ces 3
                var survivors = SuccessiveHalving(cands);

                // le 1er survivant est le meilleur
                var winner     = survivors[0];
                double wrate   = EvaluateWinRate(winner, 1000);
                string signe = (float)prop.GetValue(winner) > current ? "+" : "-";

                lock(_lock)
                {
                    if (wrate > BestWinRate)
                    {
                        BestWinRate = wrate;
                        Best        = Clone(winner);
                        improved    = true;
                        Console.Error.WriteLine(
                            $"→ Amélioration [{prop.Name}] it={iter}: winRate={wrate:P1} | Elapsed {sw.Elapsed:hh\\:mm\\:ss} - Factor {_stepFraction} - Delta {signe}{delta}"
                        );
                        LogImprovement(iter, Best, wrate);
                    }
                    else
                    {
                        Console.Error.WriteLine(
                            $"→ Pas d'Amélioration pour [{prop.Name}] it={iter}: winRate={wrate:P1} | Elapsed {sw.Elapsed:hh\\:mm\\:ss} - Factor {_stepFraction} - Delta {signe}{delta}"
                        );
                    }


                }

                iter++;
            }
            _stepFraction *= 0.5f;

            if (!improved)
            {
                Console.Error.WriteLine($"Pas d’amélioration au sweep #{sweep}, on réduit le pas.");              
            }
        }

        Console.Error.WriteLine($"Tuning terminé → meilleur winRate = {BestWinRate:P1}");
    }

    private List<TuningOptions> SuccessiveHalving(List<TuningOptions> cands)
    {
        var current = cands;
        foreach (int budget in _budgets)
        {
            // **chaque EvaluateWinRate fait tourner un Parallel.For**,
            // mais comme on crée NOUVEAUX IPlayer à chaque appel, c'est sûr
            var scored = current
                .Select(opt => (opt, rate: EvaluateWinRate(opt, budget)))
                .OrderByDescending(x => x.rate)
                .ToList();

            int keep = Math.Max(1, scored.Count -1);
            current = scored.Take(keep).Select(x => x.opt).ToList();
        }
        return current;
    }

    private double EvaluateWinRate(TuningOptions opt, int budget)
    {
        // chaque thread créera sa propre instance de A et de B
        int wins = 0;
        Parallel.For(0, budget, new ParallelOptions{
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1)
        },
        () => 0,
        (i, loop, localWins) =>
        {
            int seed = 10_000 + i;
            var sim = new MatchSimulator();

            var pA = _makeFixed();
            var pB = _makeOpponent(opt);

            var result = sim.RunMatch(pA, pB, seed);
            if (result.WinnerId == 1) localWins++;
            return localWins;
        },
        localWins => Interlocked.Add(ref wins, localWins)
        );

        return wins / (double)budget;
    }

    private static TuningOptions Clone(TuningOptions src)
    {
        var dst = new TuningOptions();
        foreach (var p in typeof(TuningOptions).GetProperties(
                     BindingFlags.Public|BindingFlags.Instance))
        {
            if (!p.CanRead || !p.CanWrite) continue;
            p.SetValue(dst, p.GetValue(src));
        }
        return dst;
    }


    private static readonly Dictionary<string,string> _tuningComments = new Dictionary<string,string>
    {
        // Poids score final
        ["DeathPenalty"]           = "Mort mort mort",
        ["TerritoryWeight"]        = "x case contrôlées",
        ["KillWeight"]             = "x kill nettes",
        ["HealthDifferenceWeight"] = "Δ somme (100–wetness)",
        ["MaxWetnessWeight"]       = "wettest ennemi",

        // Pénalités & bonus divers
        ["MultiHitPenalty"]   = "2+ agents dans même splash",
        ["DangerZonePenalty"] = "land in enemy bomb zone",
        ["HazardZonePenalty"] = "penalty for hazardZones (no‑shoot)",
        ["EarlyGamePenalty"]  = "turn<4 movement penalty",
        ["ProximityBonus"]    = "encourage rapprochement vers cibles",
        ["CoverBonus"]        = "Bonus de couverture",

        // Tuning cooldown & tir gaspillé
        ["CooldownPenaltyFactor"] = "punition somme des cooldowns",
        ["WastedShootPenalty"]    = "pour chaque shoot sans dégât",

        // Heuristiques de throw
        ["ThrowWastePenalty"]     = "malus prime pour THROW",
        ["ThrowCenterHitBonus"]   = "touche cible",
        ["ThrowAdjacentHitBonus"] = "touche case adj vide",
        ["ThrowNearEnemyBonus"]   = "ennemi adjacent"
    };

    private void LogImprovement(int iter, TuningOptions opt, double winRate)
    {
        // Assurer l'existence des dossiers
        Directory.CreateDirectory("logs");
        Directory.CreateDirectory("TuningOpt");

        // --- 1) CSV de suivi des métriques dans logs/tuning_log.csv ---
        const string csvFileName = "tuning_log.csv";
        string csvPath = Path.Combine("logs", csvFileName);

        var props = typeof(TuningOptions)
            .GetProperties()
            .Where(p => p.CanRead && p.CanWrite && p.Name != nameof(TuningOptions.TopPermutationsLimit))
            .ToArray();

        // Si nouveau fichier, écrire l'en‑tête
        if (!File.Exists(csvPath))
        {
            using var sw = new StreamWriter(csvPath, append: false);
            var headers = new[] { "Timestamp", "Iteration", "WinRate" }
                .Concat(props.Select(p => p.Name));
            sw.WriteLine(string.Join(",", headers));
        }

        // Ajouter la ligne de valeurs
        using (var sw = new StreamWriter(csvPath, append: true))
        {
            var values = new List<string>
            {
                DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                iter.ToString(),
                winRate.ToString("F3", CultureInfo.InvariantCulture)
            };
            values.AddRange(props.Select(p =>
            {
                var v = p.GetValue(opt);
                if (p.PropertyType == typeof(float))
                    return ((float)v).ToString("F4", CultureInfo.InvariantCulture);
                else
                    return v.ToString();
            }));
            sw.WriteLine(string.Join(",", values));
        }

        // --- 2) Génération du fichier C# prêt à copier/coller dans TuningOpt ---
        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        // remplacer les caractères invalides du winRate dans le nom de fichier
        string sanitizedRate = winRate.ToString("F3", CultureInfo.InvariantCulture).Replace('.', '_');
        string codeFileName = $"TuningOptions_Snapshot_{sanitizedRate}_{timestamp}.cs";
        string codePath     = Path.Combine("TuningOpt", codeFileName);

        using var csw = new StreamWriter(codePath, append: false);

        csw.WriteLine($"public class TuningOptionsSnapshot_{sanitizedRate}_{timestamp} : ITuningOptions");
        csw.WriteLine("{");

        foreach (var p in props)
        {
            var name    = p.Name;
            var comment = _tuningComments.TryGetValue(name, out var c) ? c : "";
            var valObj  = p.GetValue(opt);
            var valStr  = ((float)valObj).ToString("F4", CultureInfo.InvariantCulture) + "f";

            csw.WriteLine($"    /// <summary>{comment}</summary>");
            csw.WriteLine($"    public float {name} {{ get; set; }} = {valStr};");
            csw.WriteLine();
        }

        csw.WriteLine("    // Beam‑search / permu (fixe, non tunable)");
        csw.WriteLine("    public int TopPermutationsLimit { get; set; } = 600;");
        csw.WriteLine("}");

        Console.WriteLine($"→ métriques ajoutées dans '{csvPath}' et snapshot code dans '{codePath}'");
    }

}

static class TuningOptionsConverter
{
    public static TuningOptions FromInterface(ITuningOptions src)
    {
        var dst = new TuningOptions();
        foreach (var prop in typeof(ITuningOptions).GetProperties())
        {
            if (!prop.CanRead || !prop.CanWrite) continue;
            var value = prop.GetValue(src);
            // On sait que dans ITuningOptions toutes les propriétés sont float ou int
            prop.SetValue(dst, value);
        }
        return dst;
    }
}

public class StatisticsCollector
{
    private readonly Dictionary<int,int>             _wins         = new();
    private readonly List<int>                      _matchLengths = new();
    private readonly List<int>                      _scoreDiffs   = new();
    private readonly List<int>                      _score0       = new();
    private readonly List<int>                      _score1       = new();
    private readonly List<int>                      _terrDiffs    = new();
    private readonly List<VictoryReason>            _reasons      = new();
    private readonly List<MatchResult>              _allResults   = new();

    public void RecordResult(MatchResult r)
    {
        _allResults.Add(r);
        _matchLengths.Add(r.TurnsPlayed);
        _scoreDiffs.  Add(r.FinalPointsDiff);
        _score0.      Add(r.PointsPlayer0);
        _score1.      Add(r.PointsPlayer1);
        _terrDiffs.   Add(r.FinalTerritoryDiff);
        _reasons.     Add(r.Reason);

        if (r.WinnerId >= 0)
        {
            if (!_wins.ContainsKey(r.WinnerId))
                _wins[r.WinnerId] = 0;
            _wins[r.WinnerId]++;
        }
        else
        {
            // Treat draw as winnerId = -1
            if (!_wins.ContainsKey(-1))
                _wins[-1] = 0;
            _wins[-1]++;
        }
    }

    /// <summary>
    /// Écrit un CSV versionné avec métadatas en tête.
    /// </summary>
    public void PrintSummaryWithMetadata(
        IPlayer playerA, ITuningOptions tuningA,
        IPlayer playerB, ITuningOptions tuningB)
    {
        // 1) Calcul du prochain numéro de fichier
        string pattern = "match_results_*.csv";
        string dir     = Directory.GetCurrentDirectory();
        var files      = Directory.GetFiles(dir, pattern);
        int maxVer     = files
            .Select(Path.GetFileNameWithoutExtension)
            .Select(name => {
                var parts = name.Split('_');
                return (parts.Length == 3 && int.TryParse(parts[2], out int v)) ? v : 0;
            })
            .DefaultIfEmpty(0)
            .Max();
        int nextVer    = maxVer + 1;
        string fileName = $"match_results_{nextVer:D3}.csv";

        using var sw = new StreamWriter(fileName);

        // 2) Métadata Joueur A
        sw.WriteLine("### Joueur A ###");
        sw.WriteLine($"DecisionMaker,{playerA.GetType().Name}");
        WriteTuningOptions(sw, tuningA);
        sw.WriteLine();

        // 3) Métadata Joueur B
        sw.WriteLine("### Joueur B ###");
        sw.WriteLine($"DecisionMaker,{playerB.GetType().Name}");
        WriteTuningOptions(sw, tuningB);
        sw.WriteLine();

        // 4) Résumé agrégé
        sw.WriteLine("=== Résumé des parties ===");
        foreach (var kv in _wins.OrderBy(kv => kv.Key))
        {
            string who = kv.Key switch {
                -1  => "Draws",
                0   => "Player 0 wins",
                1   => "Player 1 wins",
                _   => $"Player {kv.Key}"
            };
            sw.WriteLine($"{who},{kv.Value}");
        }
        sw.WriteLine($"AvgLength,{_matchLengths.Average():F1}");
        sw.WriteLine($"AvgScoreDiff,{_scoreDiffs.Average():F1}");
        sw.WriteLine();

        // 5) Détail match par match
        sw.WriteLine("=== Détail match par match ===");
        sw.WriteLine("Match,Winner,Reason,Turns,Score0,Score1,ΔPoints,ΔTerritory");
        for (int i = 0; i < _allResults.Count; i++)
        {
            var r = _allResults[i];
            string winner = r.WinnerId >= 0 ? r.WinnerId.ToString() : "Draw";
            sw.WriteLine(
                $"{i+1}," +
                $"{winner}," +
                $"{r.Reason}," +
                $"{r.TurnsPlayed}," +
                $"{r.PointsPlayer0}," +
                $"{r.PointsPlayer1}," +
                $"{r.FinalPointsDiff}," +
                $"{r.FinalTerritoryDiff}"
            );
        }

        Console.WriteLine($"→ Résultats écrits dans '{fileName}'");
    }

    private void WriteTuningOptions(StreamWriter sw, ITuningOptions tuning)
    {
        // utilise reflection pour tous les props de ITuningOptions
        var props = tuning.GetType()
                          .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                          .OrderBy(p => p.Name);
        foreach (var prop in props)
        {
            var val = prop.GetValue(tuning);
            sw.WriteLine($"{prop.Name},{val}");
        }
    }

    /// <summary>
    /// Écrit un CSV dans le fichier spécifié, sans en-tête metadata.
    /// </summary>
    public void PrintSummary(string filePath)
    {
        using var sw = new StreamWriter(filePath);

        sw.WriteLine("=== Résumé des parties ===");
        foreach (var kv in _wins.OrderBy(kv => kv.Key))
        {
            string who = kv.Key switch {
                -1  => "Draws",
                0   => "Player 0 wins",
                1   => "Player 1 wins",
                _   => $"Player {kv.Key}"
            };
            sw.WriteLine($"{who},{kv.Value}");
        }
        sw.WriteLine($"AvgLength,{_matchLengths.Average():F1}");
        sw.WriteLine($"AvgScoreDiff,{_scoreDiffs.Average():F1}");
        sw.WriteLine();

        sw.WriteLine("=== Détail match par match ===");
        sw.WriteLine("Match,Winner,Reason,Turns,Score0,Score1,ΔPoints,ΔTerritory");
        for (int i = 0; i < _allResults.Count; i++)
        {
            var r = _allResults[i];
            string winner = r.WinnerId >= 0 ? r.WinnerId.ToString() : "Draw";
            sw.WriteLine(
                $"{i+1}," +
                $"{winner}," +
                $"{r.Reason}," +
                $"{r.TurnsPlayed}," +
                $"{r.PointsPlayer0}," +
                $"{r.PointsPlayer1}," +
                $"{r.FinalPointsDiff}," +
                $"{r.FinalTerritoryDiff}"
            );
        }

        Console.WriteLine($"→ Résumé et détails écrits dans '{filePath}'");
    }
}

public static class CommandParser
{
    /// <summary>
    /// Transforme les lignes "id;ACTION;ACTION" en un dictionnaire agentId→ActionSeq.
    /// </summary>
    public static Dictionary<int, ActionSeq> Parse(
        IEnumerable<string> cmds,
        GameState state,
        int myPlayerId)
    {
        var result = new Dictionary<int, ActionSeq>();
        foreach (var line in cmds)
        {
            var parts = line.Split(';');
            int agentId = int.Parse(parts[0]);
            var actions = parts.Skip(1).ToList();
            // on récupère la Position de destination si MOVE présent
            var me = state.AllAgents.First(a => a.AgentId == agentId);
            Position dest = me.Pos;
            var mv = actions.FirstOrDefault(a => a.StartsWith("MOVE "));
            if (mv != null)
            {
                var xy = mv.Substring(5).Split();
                dest = new Position(int.Parse(xy[0]), int.Parse(xy[1]));
            }
            result[agentId] = new ActionSeq(actions, dest);
        }
        return result;
    }
}

public static class CollisionResolver
{
    public static void ResolveMoves(
        GameState state,
        Dictionary<int, ActionSeq> acts1,
        Dictionary<int, ActionSeq> acts2)
    {
        // 1) Collecter uniquement les MOVE (agentId, dest)
        var moves = new Dictionary<int, Position>();
        foreach (var a in acts1.Concat(acts2))
        {
            var seq = a.Value;
            var mv = seq.Cmds.FirstOrDefault(c => c.StartsWith("MOVE "));
            if (mv != null) moves[a.Key] = seq.Dest;
        }

        // 2) Détecter conflits
        var destGroups = moves.GroupBy(kv => kv.Value)
                              .Where(g => g.Count() > 1)
                              .SelectMany(g => g.Select(kv => kv.Key))
                              .ToHashSet();

        // 3) Appliquer les déplacements non conflictuels
        foreach (var kv in moves)
        {
            if (!destGroups.Contains(kv.Key))
            {
                var agent = state.AllAgents.First(a => a.AgentId == kv.Key);
                agent.Pos = kv.Value;
            }
        }
    }
}

public static class CombatResolver
{
    public static void ApplyHunker(
        GameState state,
        Dictionary<int, ActionSeq> actions)
    {
        // Réinitialiser d'abord tous les flags
        foreach (var a in state.AllAgents)
            a.HunkeredThisTurn = false;

        // Pour chaque commande, si HUNKER_DOWN, lever le flag sur l'agent
        foreach (var kv in actions)
        {
            int agentId = kv.Key;
            var cmds    = kv.Value.Cmds;
            if (cmds.Contains("HUNKER_DOWN"))
            {
                var agent = state.AllAgents.First(a => a.AgentId == agentId);
                agent.HunkeredThisTurn = true;
            }
        }
    }

    /// <summary>
    /// Applique toutes les actions de combat (SHOOT et THROW) pour un set d'actions donné.
    /// </summary>
    public static void ApplyShootAndThrow(
        GameState state,
        Dictionary<int, ActionSeq> actions)
    {
        foreach (var kv in actions)
        {
            int attackerId = kv.Key;
            var seq        = kv.Value;
            // identifier la dernière action de combat
            var combat = seq.Cmds.LastOrDefault(c => c.StartsWith("SHOOT") || c.StartsWith("THROW"));
            if (combat == null) 
                continue;

            // récupère l'agent attaquant
            var attacker = state.AllAgents.First(a => a.AgentId == attackerId);

            if (combat.StartsWith("SHOOT"))
            {
                // parse targetId
                int targetId = int.Parse(combat.Substring(6));
                var target   = state.AllAgents.First(a => a.AgentId == targetId);

                // (2) calcul de la distance
                int dist = attacker.Pos.ManhattanDistance(target.Pos);

                if(dist <= 2 * attacker.OptimalRange)
                {
                    int initialCd = state.InitialAgents
                                        .First(d => d.AgentId == attackerId)
                                        .ShootCooldown;
                    attacker.Cooldown = initialCd + 1;
                }

                // (3) raw damage selon portée
                float raw = attacker.SoakingPower
                          * (dist <= attacker.OptimalRange
                              ? 1f : ( dist <= 2 * attacker.OptimalRange ? 0.5f : 0));

                // (4) protection par couverture
                float prot = state.CalcCoverProtection(target.Pos, attacker.Pos);

                // (5) bonus Hunker
                if (target.HunkeredThisTurn)
                    prot += 0.25f;

                prot = Math.Min(prot, 1);

                // (6) appliquer wetness
                int dmg = (int)Math.Ceiling(raw * (1f - prot));
                target.Wetness += dmg;
            }
            else // THROW
            {
                // parse coords
                var parts = combat.Split(' ');
                int tx = int.Parse(parts[1]), ty = int.Parse(parts[2]);

                // consomme une bombe
                attacker.SplashBombs = Math.Max(0, attacker.SplashBombs - 1);

                // zone Chebyshev ≤ 1
                foreach (var targ in state.AllAgents)
                {
                    int ddx = Math.Abs(targ.Pos.X - tx);
                    int ddy = Math.Abs(targ.Pos.Y - ty);
                    if (Math.Max(ddx, ddy) <= 1)
                    {
                        targ.Wetness += 30;
                    }
                }
            }
        }
    }
}

public enum VictoryReason
{
    Elimination,    // élimination de tous les ennemis
    PointLead,      // avance ≥ 600 points en cours de partie
    EndOfTurns,      // victoire au décompte des 100 tours

    MatchNul
}

public class MatchResult
{
    public int WinnerId            { get; }
    public int TurnsPlayed         { get; }
    public int PointsPlayer0       { get; }
    public int PointsPlayer1       { get; }
    public int FinalPointsDiff     { get; }
    public int FinalTerritoryDiff  { get; }
    public VictoryReason Reason    { get; }
    public GameState FinalState    { get; }

    public bool IsDraw => WinnerId < 0;

    public MatchResult(int winnerId,
                       int turnsPlayed,
                       GameState finalState,
                       VictoryReason reason)
    {
        WinnerId    = winnerId;
        TurnsPlayed = turnsPlayed;
        FinalState  = finalState.Clone();
        Reason      = reason;

        // points cumulés
        PointsPlayer0   = finalState.PointsByPlayer[0];
        PointsPlayer1   = finalState.PointsByPlayer[1];
        FinalPointsDiff = PointsPlayer0 - PointsPlayer1;

        // diff de territoire instantanée en fin de partie
        var helper = new TerritoryHelper(finalState);
        helper.PrecomputeEnemyDistances();
        var myFinals = finalState
            .MyAgents
            .Select(a => (agent: a, dest: a.Pos))
            .ToList();
        FinalTerritoryDiff = helper.ComputeTerritoryDiffOptimized(myFinals);
    }

    public static MatchResult Draw(int turnsPlayed, GameState finalState)
        => new MatchResult(-1, turnsPlayed, finalState, VictoryReason.MatchNul);
}

class Program
{
    static void Main(string[] args)
    {
        var sim   = new MatchSimulator();
        var stats = new StatisticsCollector();  
        TuningOptions tuningA = new TuningOptions();
        TuningOptions tuningB = TuningOptionsConverter.FromInterface(new TuningOptions548());   
        // 2) Factories pour créer à la volée vos deux joueurs
        Func<IPlayer> makeA = () =>
        {
            var dmA     = new MakeDecisionPermuted(tuningA);
            return new CodinGameAdapter(dmA, false, timeLimitMs: 20, topX: 350);
        };
        Func<TuningOptions, IPlayer> makeB = opt =>
        {
            var dmB = new MakeDecisionPermuted(opt);
            return new CodinGameAdapter(dmB, false, timeLimitMs: 20, topX: 350);
        };  

        if (args.Length > 0 && args[0].Equals("tuneDescent", StringComparison.OrdinalIgnoreCase))
        {
            // 3) Instanciation du tuner avec factory pour A et factory paramétrée pour B
            var tuner = new Tuner(
                start: tuningB,
                makeFixedPlayer: makeA,
                makePlayer: makeB,
                matchesPerBatch: 600
            );

            // 4) Lancement
            tuner.Run(maxIters: 250);
            tuningB = tuner.CurrentBest;


            // 4) On instancie et on lance le tuner
            var tunerDescent = new CoordinateDescentTuner(
                start: tuningB, // point de départ
                makeFixedPlayer: makeA,               // usine pour générer A
                makeOpponent: makeB,               // usine pour générer B à partir de chaque opt
                budgets: new[] { 160 }, // paliers de successive‑halving
                stepFraction: 0.5f,                // ±10% par pas
                maxSweeps: 4                   // nombre de parcours sur tous les paramètres
            );
            tunerDescent.Run();
            tuningB = tunerDescent.Best;
            // tuner.Best contient les meilleurs tuning
            // tuner.BestWinRate le win‐rate max observé
        }
        //TuningOptions tuningA = new TuningOptions(){ HazardZonePenalty = 10, TerritoryWeight = 8f };           
        //tuningB = TuningOptionsConverter.FromInterface(new TuningOptions565());

        // Vos decision makers
        IDecisionMaker dmAFinal = new MakeDecisionPermuted(tuningA);
        IDecisionMaker dmBFinal = new MakeDecisionPermuted(tuningB);

        // Les adapter en IPlayer pour le simulateur
        IPlayer playerA = new CodinGameAdapter(dmAFinal, false, timeLimitMs: 50, topX: 600);
        IPlayer playerB = new CodinGameAdapter(dmBFinal, false, timeLimitMs: 50, topX: 600);

        const int NB_MATCHES = 2000;
        const int SEED_START = 1000;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < NB_MATCHES; i++)
        {
            var result = sim.RunMatch(playerA, playerB, SEED_START + i);
            stats.RecordResult(result);
            Console.Error.WriteLine($"Match {i} Completed.  Elapsed {sw.Elapsed:hh\\:mm\\:ss}");
        }
        sw.Stop();

        stats.PrintSummaryWithMetadata(playerA, tuningA, playerB, tuningB);
    }
}
