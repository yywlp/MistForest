using System;
using System.Collections.Generic;
using System.Linq;
using MistForest.Core.Consts;
using MistForest.Core.GameMap;
using MistForest.Core.GameRefactored.Roles;
using MistForest.Core.Utils;
using ZLinq;
using static MistForest.Core.GameRefactored.Game;
using static MistForest.Core.GameRefactored.PlayerBuilder;

namespace MistForest.Core.GameRefactored
{
    public class Game
    {
        private readonly GameMode _mode;
        public Board Board { get; private set; }
        public Role[] Roles { get; private set; }
        private readonly Unit[] _units;
        public readonly Player[] Players;
        public List<Building> Buildings;
        public List<Event> Events;
        private readonly GameCount _gameCount;
        public int CurrentPlayerId { get; private set; }
        private Bitset _roundActed;
        private int _turnCount;
        private int _roundCount;
        private GameState _gameState;
        private bool _isGameOver => _gameState is GameState.Win or GameState.Draw;
        public bool IsTeam => _gameCount is GameCount.TeamGameCount;

        public abstract class GameCount
        {
            private int _rolesPerPlayer;
            public int PlayerCount { get; private set; }

            public class SingleGameCount : GameCount
            {
                public static SingleGameCount New(int rolesPerPlayer, int playerCount)
                {
                    return new() { _rolesPerPlayer = rolesPerPlayer, PlayerCount = playerCount };
                }
            }

            public class TeamGameCount : GameCount
            {
                public int TeamCount { get; private set; }
                public int PlayersPerTeam { get; private set; }
                public new int PlayerCount => TeamCount * PlayersPerTeam;

                public static TeamGameCount New(
                    int rolesPerPlayer,
                    int teamCount,
                    int playersPerTeam
                )
                {
                    return new()
                    {
                        _rolesPerPlayer = rolesPerPlayer,
                        TeamCount = teamCount,
                        PlayersPerTeam = playersPerTeam,
                    };
                }
            }
        }

        private Game(
            GameMode mode,
            Board board,
            Role[] roles,
            Unit[] units,
            Player[] players,
            List<Building> buildings,
            GameCount gameCount,
            List<Event> events,
            Bitset roundActed,
            GameState gameState
        )
        {
            _mode = mode;
            Board = board;
            Roles = roles;
            _units = units;
            Players = players;
            Buildings = buildings;
            _gameCount = gameCount;
            Events = events;
            _roundActed = roundActed;
            _gameState = gameState;
        }

        public static Game New(
            GameMode mode,
            Board board,
            Role[] roles,
            Unit[] units,
            Player[] players,
            GameCount gameCount,
            int firstPlayerId
        )
        {
            Game game = new(
                mode,
                board,
                roles,
                units,
                players,
                buildings: new List<Building>(),
                gameCount,
                events: new List<Event>(),
                roundActed: Bitset.WithCapacity(
                    gameCount switch
                    {
                        GameCount.SingleGameCount singleCountInner => singleCountInner.PlayerCount,
                        GameCount.TeamGameCount teamCountInner => teamCountInner.PlayerCount,
                        _ => throw new NotImplementedException(),
                    }
                ),
                gameState: GameState.Playing.Instance
            )
            {
                CurrentPlayerId = firstPlayerId,
            };
            game.PrepareTurn();
            return game;
        }

        public int PlayerIdFromUnitId(int unitId)
        {
            return Roles[_units[unitId].RoleId].PlayerId;
        }

        /// <summary>
        /// Reports whether the player's turn in the current round has ended.
        /// </summary>
        internal bool HasPlayerActedThisRound(int playerId) => _roundActed[playerId];

        public bool IsPlayerAllied(int playerA, int playerB)
        {
            return IsTeam switch
            {
                true => Players[playerA].TeamId == Players[playerB].TeamId,
                false => playerA == playerB,
            };
        }

        public int NextPlayerId(int playerId)
        {
            if (_gameCount is GameCount.SingleGameCount singleCountInner)
            {
                return (playerId + 1) % singleCountInner.PlayerCount;
            }
            else if (_gameCount is GameCount.TeamGameCount teamCountInner)
            {
                if (
                    playerId + teamCountInner.PlayersPerTeam
                    >= teamCountInner.TeamCount * teamCountInner.PlayersPerTeam
                )
                {
                    return (playerId + 1 + teamCountInner.PlayersPerTeam)
                        % (teamCountInner.TeamCount * teamCountInner.PlayersPerTeam)
                        % teamCountInner.PlayersPerTeam;
                }
                else
                {
                    return playerId + teamCountInner.PlayersPerTeam;
                }
            }
            throw new NotImplementedException();
        }

        public void AddBuilding(BuildingBuilder buildingBuilder)
        {
            Building building = buildingBuilder.SetId(Buildings.Count)!.Build()!;
            Board.GetGrid(building.Pos)!.GridBuilding = building.Id;
            Buildings.Add(building);
        }

        public bool ExecuteAction(Action action)
        {
            if (_isGameOver)
            {
                return false;
            }
            if (action.CallerUnitId < 0 || action.CallerUnitId >= _units.Length)
            {
                return false;
            }
            Unit caller = _units[action.CallerUnitId];
            int playerId = PlayerIdFromUnitId(action.CallerUnitId);
            if (playerId != CurrentPlayerId)
            {
                return false;
            }
            if (
                caller.Alive is false
                || caller.HasAction(action) is false
                || _mode switch
                {
                    GameMode.Scout => action is not IScoutAction,
                    _ => action is IScoutOnlyAction,
                }
            )
            {
                return false;
            }
            if (
                Players[playerId]
                    .Roles.AsValueEnumerable()
                    .Where(role => role.Id != caller.RoleId && role.Alive)
                    .Any(role => role.HasStartedTurn)
            )
            {
                return false;
            }
            Role role = Roles[caller.RoleId];
            if (role.JudgeAction(action, this))
            {
                role.ExecuteAction(action, this);

                _gameState = CheckVictory();
                if (_isGameOver)
                {
                    return true;
                }

                if (role.HasActionLeft(this) is false)
                {
                    EndTurn();
                }
                return true;
            }
            return false;
        }

        public Event[] GetEventsForPlayer(int playerId)
        {
            if (playerId < 0 || playerId >= Players.Length)
            {
                return Array.Empty<Event>();
            }
            return Events.AsValueEnumerable().Where(entry => entry.IsVisibleTo(playerId)).ToArray();
        }

        private void EndTurn()
        {
            ShadowAttachment.EndTurn(CurrentPlayerId, this);
            ++_turnCount;
            _roundActed[CurrentPlayerId] = true;

            bool nextRound = Players
                .AsValueEnumerable()
                .Where(player => player.Alive)
                .All(player => _roundActed[player.Id]);
            if (nextRound)
            {
                foreach (
                    (_, int index) in Players
                        .AsValueEnumerable()
                        .WithIndex()
                        .Where(entry => entry.item.Alive)
                )
                {
                    _roundActed[index] = false;
                }
                ++_roundCount;
                foreach (Unit unit in _units.Where(unit => unit.Alive))
                {
                    unit.EndRound(this);
                }
                foreach (Building building in Buildings)
                {
                    building.EndRound();
                }
            }
            _gameState = CheckVictory();
            if (_isGameOver)
            {
                return;
            }
            do
            {
                CurrentPlayerId = NextPlayerId(CurrentPlayerId);
                PrepareTurn();
            } while (!Players[CurrentPlayerId].Alive);
        }

        internal void NotifyUnitMoved(Unit unit, Board.Node from, Board.Node to)
            => ShadowAttachment.OnMoved(unit, from, to, this);

        internal void ClearShadowLinks(int unitId) => ShadowAttachment.RemoveUnit(unitId, this);

        public void PrepareTurn()
        {
            foreach (Role role in Players[CurrentPlayerId].Roles)
            {
                foreach (Unit unit in role.GetUnits())
                {
                    if (unit.Alive)
                    {
                        unit.PrepareTurn(this);
                    }
                }
            }
            for (int i = 1; i <= Board.Height; ++i)
            {
                for (int j = 1; j <= Board.Width; ++j)
                {
                    Grid grid = Board.GetGrid(Board.At(i, j)!.Value)!;
                    if (
                        grid.GridBuilding is int buildingId
                        && Buildings[buildingId] is Building.Rubble rubble
                        && rubble.Owner == CurrentPlayerId
                    )
                    {
                        rubble.PrepareTurn(this);
                    }
                }
            }
        }

        public abstract record GameState
        {
            public record Playing : GameState
            {
                public static readonly Playing Instance = new();

                public static Playing New() => new();
            }

            public record Win : GameState
            {
                public int WinnerId { get; private set; }

                public static Win New(int winnerId)
                {
                    return new() { WinnerId = winnerId };
                }
            }

            public record Draw : GameState
            {
                public static readonly Draw Instance = new();

                public static Draw New() => new();
            }
        }

        private GameState CheckVictory()
        {
            if (_gameCount is GameCount.SingleGameCount singleCountInner)
            {
                int? firstAlive = Players
                    .AsSpan()
                    .AsEnumerable()
                    .FirstOrDefault(player => player.Alive)
                    ?.Id;
                if (firstAlive is null)
                {
                    return GameState.Draw.Instance;
                }
                if (
                    Players
                        .AsSpan((firstAlive!.Value + 1)..)
                        .AsEnumerable()
                        .Any(player => player.Alive)
                )
                {
                    if (_roundCount >= GameConsts.MaxRoundCount)
                    {
                        return GameState.Draw.New();
                    }
                    return GameState.Playing.Instance;
                }
                else
                {
                    return GameState.Win.New(firstAlive.Value);
                }
            }
            else if (_gameCount is GameCount.TeamGameCount teamCountInner)
            {
                int firstAliveId = (0..teamCountInner.TeamCount)
                    .AsValueEnumerable()
                    .FirstOrDefault(
                        teamId =>
                            GetTeamPlayers(teamId).AsEnumerable().Any(player => player.Alive),
                        defaultValue: -1
                    );
                if (firstAliveId == -1)
                {
                    return GameState.Draw.Instance;
                }
                if (
                    ((firstAliveId + 1)..teamCountInner.TeamCount)
                        .AsValueEnumerable()
                        .Any(teamId =>
                            GetTeamPlayers(teamId).AsEnumerable().Any(player => player.Alive)
                        )
                )
                {
                    if (_roundCount >= GameConsts.MaxRoundCount)
                    {
                        return GameState.Draw.New();
                    }
                    return GameState.Playing.Instance;
                }
                else
                {
                    return GameState.Win.New(firstAliveId);
                }
            }
            throw new NotImplementedException();
        }

        private ReadOnlySpan<Player> GetTeamPlayers(int teamId)
        {
            return Players.AsSpan(
                (teamId * PlayerConsts.MaxPlayersPerTeam)..(
                    (teamId + 1) * PlayerConsts.MaxPlayersPerTeam
                )
            );
        }

        public Unit? GetUnit(int unitId)
        {
            if (unitId >= _units.Length || unitId < 0)
            {
                return null;
            }
            return _units[unitId];
        }
    }

    public class GameBuilder
    {
        private GameMode? _mode;
        private Board? _board;
        private Player[]? _players;
        private Role[]? _roles;
        private Unit[]? _units;
        private int? _rolesPerPlayer;
        private int? _playerCount;
        private int? _teamCount;
        private int? _playersPerTeam;
        private bool _isTeamGame;
        private int? _firstPlayerId;

        public GameBuilder? SetMode(Box<GameMode> mode)
        {
            if (_mode is not null || _board is not null)
            {
                return null;
            }
            if (mode.TryTake() is not GameMode modeInner)
            {
                return null;
            }
            _mode = modeInner;
            _board = modeInner switch
            {
                GameMode.Scout or GameMode.Heroes => Board.FromMode(modeInner, out _),
                _ => null,
            };
            return this;
        }

        public GameBuilder? SetBoard(Box<Board> board)
        {
            if (_mode is null || _board is not null)
            {
                return null;
            }
            if (board.TryTake() is not Board boardInner)
            {
                return null;
            }
            _board = boardInner;
            return this;
        }

        public GameBuilder? SetPlayers(Box<List<PlayerBuilder>> players)
        {
            if (_players is not null || _board is null || _mode is null)
            {
                return null;
            }
            if (players.TryTake() is not List<PlayerBuilder> playersInner)
            {
                return null;
            }
            if (playersInner.Count < 2 || playersInner.Count > PlayerConsts.MaxPlayerCount)
            {
                return null;
            }
            int rolesPerPlayer = playersInner[0].Roles.Count();
            if (_mode is GameMode.Scout && rolesPerPlayer != 1)
            {
                return null;
            }
            if (
                playersInner
                    .AsValueEnumerable()
                    .Any(player =>
                        player.TeamId is not null
                        || player.Id is not null
                        || player.Roles.Count() != rolesPerPlayer
                        || _mode is GameMode.Scout && player.Roles[0] is not SniperBuilder
                        || player.Roles.Any(role => !role.CanInitialStay(_board!))
                    )
            )
            {
                return null;
            }

            var playerBuilders = playersInner
                .AsValueEnumerable()
                .Select((player, index) => player.SetId(index)!)
                .ToList();
            var roleBuilders = playerBuilders
                .AsValueEnumerable()
                .SelectMany(player =>
                    player
                        .Roles.AsValueEnumerable()
                        .Select(role => role.SetPlayerId(player.Id!.Value)!)
                )
                .Select((role, index) => role.SetId(index)!)
                .ToList();
            var unitBuilders = roleBuilders
                .AsValueEnumerable()
                .SelectMany(role =>
                    role.UnitBuilder.AsValueEnumerable()
                        .Select(unit => unit.SetRoleId(role.Id!.Value)!)
                )
                .Select((unit, index) => unit.SetId(index)!)
                .Select(unit =>
                {
                    if (unit is SniperUnitBuilder sniperUnit)
                    {
                        int range =
                            _mode is GameMode.Scout
                                ? SniperConsts.SniperAttackRangeScout
                                : SniperConsts.SniperAttackRange;

                        sniperUnit.SetAttackRange(range);
                    }

                    return unit;
                })
                .ToList();
            _rolesPerPlayer = rolesPerPlayer;
            _playerCount = playersInner.Count;
            _roles = roleBuilders.AsValueEnumerable().Select(role => role.Build()!).ToArray();
            _units = unitBuilders.AsValueEnumerable().Select(unit => unit.Build()!).ToArray();
            foreach (Unit unit in _units)
            {
                _board!.GetGrid(unit.Position)!.Units.Add(unit.Id);
            }
            _players = playerBuilders
                .AsValueEnumerable()
                .Select(player => player.Build()!)
                .ToArray();
            _isTeamGame = false;
            return this;
        }

        public GameBuilder? SetTeams(Box<List<Team>> teams)
        {
            if (_players is not null || _board is null || _mode is null or GameMode.Scout)
            {
                return null;
            }
            if (teams.TryTake() is not List<Team> teamsInner)
            {
                return null;
            }
            if (teamsInner.Count < 2 || teamsInner.Count > PlayerConsts.MaxTeamCount)
            {
                return null;
            }
            int playersPerTeam = teamsInner[0].Players.Count();
            if (
                playersPerTeam < 2
                || playersPerTeam > PlayerConsts.MaxPlayersPerTeam
                || teamsInner.Any(team => team.Players.Count() != playersPerTeam)
            )
            {
                return null;
            }
            int rolesPerPlayer = teamsInner[0].Players[0].Roles.Count();
            var playersInner = teamsInner.SelectMany(team => team.Players).ToList();
            if (
                playersInner.Any(player =>
                    player.TeamId is not null
                    || player.Id is not null
                    || player.Roles.Count() != rolesPerPlayer
                    || player.Roles.Any(role => !role.CanInitialStay(_board!))
                )
            )
            {
                return null;
            }
            foreach (
                var (team, teamId) in teamsInner
                    .AsValueEnumerable()
                    .Select((team, index) => (team, index))
            )
            {
                team.Players.ForEach(player => player.SetTeamId(teamId));
            }
            var playerBuilders = playersInner
                .AsValueEnumerable()
                .Select((player, index) => player.SetId(index)!)
                .ToList();
            var roleBuilders = playerBuilders
                .AsValueEnumerable()
                .SelectMany(player =>
                    player
                        .Roles.AsValueEnumerable()
                        .Select(role => role.SetPlayerId(player.Id!.Value)!)
                )
                .Select((role, index) => role.SetId(index)!)
                .ToList();
            var unitBuilders = roleBuilders
                .AsValueEnumerable()
                .SelectMany(role =>
                    role.UnitBuilder.AsValueEnumerable()
                        .Select(unit => unit.SetRoleId(role.Id!.Value)!)
                        .Select(unit =>
                            unit is SniperUnitBuilder sniperUnit
                                ? sniperUnit.SetAttackRange(SniperConsts.SniperAttackRange)
                                : unit
                        )
                )
                .Select((unit, index) => unit.SetId(index)!)
                .ToList();
            _rolesPerPlayer = rolesPerPlayer;
            _teamCount = teamsInner.Count;
            _playersPerTeam = playersPerTeam;
            _roles = roleBuilders.AsValueEnumerable().Select(role => role.Build()!).ToArray();
            _units = unitBuilders.AsValueEnumerable().Select(unit => unit.Build()!).ToArray();
            foreach (Unit unit in _units)
            {
                _board!.GetGrid(unit.Position)!.Units.Add(unit.Id);
            }
            _players = playerBuilders
                .AsValueEnumerable()
                .Select(player => player.Build()!)
                .ToArray();
            _isTeamGame = true;
            return this;
        }

        public GameBuilder? SetFirstPlayerId(int firstPlayerId)
        {
            if (_players is null)
            {
                return null;
            }
            int playerCount = _isTeamGame
                ? _playersPerTeam!.Value * _teamCount!.Value
                : _playerCount!.Value;
            if (firstPlayerId < 0 || firstPlayerId >= playerCount)
            {
                return null;
            }
            _firstPlayerId = firstPlayerId;
            return this;
        }

        public Game? Build()
        {
            if (_mode is null || _board is null || _players is null)
            {
                return null;
            }
            GameCount gameCount = _isTeamGame switch
            {
                true => GameCount.TeamGameCount.New(
                    _rolesPerPlayer!.Value,
                    _teamCount!.Value,
                    _playersPerTeam!.Value
                ),
                false => GameCount.SingleGameCount.New(_rolesPerPlayer!.Value, _playerCount!.Value),
            };
            return New(
                mode: _mode,
                board: _board,
                roles: _roles!,
                units: _units!,
                players: _players,
                gameCount: gameCount,
                firstPlayerId: _firstPlayerId switch
                {
                    null => 0,
                    int firstPlayerIdInner => firstPlayerIdInner,
                }
            );
        }
    }
}
