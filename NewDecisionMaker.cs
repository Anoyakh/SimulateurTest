using System.Diagnostics;

public class NewDecisionMaker : IDecisionMaker
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

    private AgentState _bestEnemyThisTurn;
    private AgentState _bestAllyThisTurn;

    private readonly int HazardousStart = 0;

    public NewDecisionMaker(GameState state, TuningOptions tuning)
    {
        _initialState = state;
        Tuning = tuning;
    }

    public NewDecisionMaker(TuningOptions tuning)
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
        _territoryHelper.PrecomputeEnemyDistances(_initialState);
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
        _territoryHelper.PrecomputeEnemyDistances(_initialState);

        // 3) Appeler votre DecideFullEval existant
        return DecideFullEval(state.Turn == 1 ? 500 : timeLimitMs, topX, true);
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

    private HashSet<Position> EstimateEnemyBombZones(GameState state)
    {
        var dangerZones = new HashSet<Position>();
        foreach (var e in state.EnemyAgents.Where(e => e.SplashBombs > 0))
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

    private (float score, GameState state) EvaluateFullTurn(GameState baseState, List<(AgentState agent, ActionSeq seq)> friendly, HashSet<Position> dangerZones, List<Position> throwTargets)
    {
        //var swEval = Stopwatch.StartNew();
        var sim = baseState.Clone();

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


        if (_bestEnemyThisTurn != null)
        {
            score += 0.3f * _bestEnemyThisTurn.Wetness;
            if (_bestEnemyThisTurn.Wetness >= 100)
                score += 20;  
        }

        if (_bestAllyThisTurn != null)
        {
            score += 0.2f * (100 - _bestAllyThisTurn.Wetness);
            if (_bestAllyThisTurn.Wetness >= 100)
                score += -30;
        }

        if(myHealth <= 0) score -= Tuning.DeathPenalty;
        if(theirHealth <= 0) score += Tuning.DeathPenalty;

        //Console.Error.WriteLine("Eval - End :" + 1000 * (decimal)swEval.ElapsedTicks/(decimal)Stopwatch.Frequency + "ms");

        return (score, sim);
    }

    private List<int[]> GetTopKJointActions( List<AgentState> agents,Dictionary<int, List<ActionSeq>> actionsCache,Dictionary<int, List<float>>     evalCache,int k)
    {
        int n = agents.Count;
        var pq   = new PriorityQueue<(int[] idx, float sum), float>();
        var seen = new HashSet<string>();

        int[] start = Enumerable.Repeat(0, n).ToArray();
        float sum0 = agents.Select((a,i) => evalCache[a.AgentId][0]).Sum();
        pq.Enqueue((start, sum0), -sum0);
        seen.Add(string.Join(",", start));

        var result = new List<int[]>();
        while (pq.Count > 0 && result.Count < k)
        {
            var (cur, _) = pq.Dequeue();
            result.Add((int[])cur.Clone());

            for (int i = 0; i < n; i++)
            {
                if (cur[i] + 1 < evalCache[agents[i].AgentId].Count)
                {
                    var nxt = (int[])cur.Clone();
                    nxt[i]++;
                    var key = string.Join(",", nxt);
                    if (seen.Add(key))
                    {
                        float newSum = cur
                            .Select((ci,j) => evalCache[agents[j].AgentId][ci])
                            .Sum()
                        - evalCache[agents[i].AgentId][cur[i]]
                        + evalCache[agents[i].AgentId][nxt[i]];

                        pq.Enqueue((nxt, newSum), -newSum);
                    }
                }
            }
        }
        return result;
    }

    private List<Position> EstimateEnemyThrowTargets(GameState state)
    {
        var targets = new HashSet<Position>();
        int w = state.Width, h = state.Height;

        foreach (var e in state.EnemyAgents.Where(e => e.SplashBombs > 0))
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
        var dangerZones    = EstimateEnemyBombZones(_initialState);
        var throwTargets = EstimateEnemyThrowTargets(_initialState);

        var forces = _initialState.AllAgents
            .Select(a =>
            {
                int globalCd = _initialState.InitialAgents
                                .First(d => d.AgentId == a.AgentId)
                                .ShootCooldown;
                float force = a.SoakingPower / (globalCd + 1f)
                            * ((a.OptimalRange + 4f) / 8f);
                return (agent: a, force);
            })
            .Where(x => x.agent.Wetness < 100)  // on ignore déjà éliminés
            .ToList();

        // Meilleure cible ennemie
        _bestEnemyThisTurn = forces
            .Where(x => x.agent.PlayerId != _initialState.MyPlayerId)
            .OrderByDescending(x => x.force)
            .Select(x => x.agent)
            .FirstOrDefault();

        // Meilleur allié
        _bestAllyThisTurn = forces
            .Where(x => x.agent.PlayerId == _initialState.MyPlayerId)
            .OrderByDescending(x => x.force)
            .Select(x => x.agent)
            .FirstOrDefault();

        // 4) Initialisation de la recherche permutée
        int phase1Budget = Math.Max(40, timeLimitMs - 10);
        var pq   = new PriorityQueue<(int[] idx, float h), float>();
        var seen = new HashSet<string>();
        int[] startIdx = Enumerable.Repeat(0, n).ToArray();
        float startH   = topActions.Select((list,i) => _myEvalCache[_myAgents[i].AgentId][0]).Sum();
        pq.Enqueue((startIdx, startH), -startH);
        seen.Add(string.Join(",", startIdx));

        // on stocke les 10 meilleurs candidats (idx1, score1, state1)
        var topCandidates = new List<(int[] idx1, float score1, GameState state1)>();


        while (pq.Count > 0 && sw.ElapsedMilliseconds < phase1Budget)
        {
            var (cur, _) = pq.Dequeue();

            // 1) on construit la joint‐action courante
            var friendly = Enumerable.Range(0, n)
                .Select(i => (_myAgents[i], topActions[i][cur[i]]))
                .ToList();

            // 2) on évalue complètement ce tour
            var (score1, state1) = EvaluateFullTurn(
                _initialState, friendly,
                dangerZones, throwTargets
            );

            // 3) on garde ce candidat si c’est dans le top‑10
            if (topCandidates.Count < 10 ||
                score1 > topCandidates.Min(x => x.score1))
            {
                topCandidates.Add((cur, score1, state1));
                if (topCandidates.Count > 10)
                    topCandidates.Remove(
                        topCandidates.OrderBy(x => x.score1).First()
                    );
            }

            // 4) on génère les voisins (beam) comme avant, sur l’heuristique h
            for (int i = 0; i < n; i++)
            {
                if (cur[i] + 1 < topActions[i].Count)
                {
                    var nxt = (int[])cur.Clone();
                    nxt[i]++;
                    var key = string.Join(",", nxt);
                    if (seen.Add(key))
                    {
                        float newH = cur
                        .Select((ci,j) => _myEvalCache[_myAgents[j].AgentId][ci])
                        .Sum()
                        - _myEvalCache[_myAgents[i].AgentId][cur[i]]
                        + _myEvalCache[_myAgents[i].AgentId][nxt[i]];

                        pq.Enqueue((nxt, newH), -newH);
                    }
                }
            }
        }

        //Console.Error.WriteLine($"DecideFullEval iters: {nbIter}, time: {sw.ElapsedMilliseconds}ms");
        //Console.Error.WriteLine("Best Eval was at Iteration " + bestIter);
        //Console.Error.WriteLine("Best Score : " + (double)bestScore);
        // 6) Construire les commandes à partir de bestIdxs
        if (topCandidates.Count == 0)
        // fallback
        return _myAgents.Select(a => $"{a.AgentId};HUNKER_DOWN").ToList();

        // on trie par score décroissant
        topCandidates.Sort((a,b) => b.score1.CompareTo(a.score1));
        var bestFirstIdx1 = topCandidates[0].idx1;

        // ─── PHASE 2 (restant) : second lookahead réduit ───
        float bestTotal = float.NegativeInfinity;
        int[] bestIdx1  = null;

        //Console.Error.WriteLine("First Turn : " + sw.ElapsedMilliseconds + "ms");

        foreach (var (idx1, score1, state1) in topCandidates.Take(3))
        {
            if (sw.ElapsedMilliseconds >= timeLimitMs) break;
            var lookaheadState = state1.Clone();
        // on peut créer un nouvel objet pour le cache :
            var lookaheadDM = new NewDecisionMaker(lookaheadState, Tuning);
            lookaheadDM._territoryHelper = new TerritoryHelper(lookaheadState);
            lookaheadDM._territoryHelper.PrecomputeEnemyDistances(lookaheadState);
            lookaheadDM.RecomputePerTurnCaches();

            // Danger zones & throw targets sur lookaheadState
            var dangerZones1   = lookaheadDM.EstimateEnemyBombZones(lookaheadState);
            var throwTargets1  = lookaheadDM.EstimateEnemyThrowTargets(lookaheadState);
            // a) précompute local pour le tour 2 à partir de state1
            var actions2 = new Dictionary<int, List<ActionSeq>>();
            var evals2   = new Dictionary<int, List<float>>();
            var occ2     = new HashSet<Position>(state1.AllAgents.Select(a => a.Pos));

            foreach (var me in state1.MyAgents)
            {
                if (sw.ElapsedMilliseconds >= timeLimitMs) break;
                var seqs2 = lookaheadDM.GenerateActionSeqs(me, occ2);
                actions2[me.AgentId] = seqs2;
                evals2[me.AgentId]   = seqs2
                    .Select(s => lookaheadDM.EvaluateIndividual(me, s, state1))
                    .ToList();
            }
            if (sw.ElapsedMilliseconds >= timeLimitMs) break;
            // b) on récupère les top‑5 joint actions sur tour 2
            var top5 = lookaheadDM.GetTopKJointActions(state1.MyAgents, actions2, evals2, 10);

            // c) on les simule + greedy adverse + évalue
            foreach (var idx2 in top5)
            {
                if (sw.ElapsedMilliseconds >= timeLimitMs) break;
                var friendly2 = Enumerable.Range(0, n)
                    .Select(i => (state1.MyAgents[i], actions2[state1.MyAgents[i].AgentId][idx2[i]]))
                    .ToList();

                var (score2, _) = lookaheadDM.EvaluateFullTurn(
                    state1, friendly2,
                    dangerZones1, throwTargets1
                );

                float total = score1 + score2;
                if (total > bestTotal)
                {
                    bestTotal = total;
                    bestIdx1  = idx1;
                }
            }
        }

        //Console.Error.WriteLine("Second Turn : " + sw.ElapsedMilliseconds + "ms");

        // si jamais on n’a rien trouvé, fallback
        if (bestIdx1 == null)
            bestIdx1 = bestFirstIdx1;

        // ─── reconstruction des commandes du premier tour ───
        var result = new List<string>();
        for (int i = 0; i < n; i++)
        {
            var me  = _myAgents[i];
            var seq = topActions[i][bestIdx1[i]];
            result.Add($"{me.AgentId};{string.Join(";", seq.Cmds)}");
        }
        return result;
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
