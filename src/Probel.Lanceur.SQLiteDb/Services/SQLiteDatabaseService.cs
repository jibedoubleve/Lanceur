﻿using Dapper;
using Probel.Lanceur.Core.Entities;
using Probel.Lanceur.Core.Plugins;
using Probel.Lanceur.Core.Services;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Data.SQLite;
using System.Linq;

namespace Probel.Lanceur.SQLiteDb.Services
{
    public partial class SQLiteDatabaseService : IDataSourceService
    {
        #region Fields

        private readonly string _connectionString;
        private readonly IReservedKeywordService _keywordService;
        private readonly ILogService _log;
        private readonly IPluginManager _pluginManager;
        private readonly IReservedKeywordService _reservedKeywordService;

        #endregion Fields

        #region Constructors

        public SQLiteDatabaseService(IReservedKeywordService keywordService,
            ILogService log,
            IReservedKeywordService reservedKeywordService,
            IPluginManager pluginManager,
            IConnectionStringManager csm
            )
        {
            _pluginManager = pluginManager;
            _reservedKeywordService = reservedKeywordService;
            _log = log;
            _connectionString = csm.Get();
            _keywordService = keywordService;
        }

        #endregion Constructors

        #region Methods

        public void Clear()
        {
            using (var c = BuildConnection())
            {
                var sql = @"
                    delete from alias;
                    delete from alias_name;
                    delete from alias_usage;
                    delete from alias_session where name = 'SlickRun';";
                c.Execute(sql);
            }
        }

        public void Create(Alias s, IEnumerable<string> names = null)
        {
            s.Normalise();
            var sql = @"
                insert into alias (
                    arguments,
                    file_name,
                    notes,
                    run_as,
                    start_mode,
                    id_session
                ) values (
                    @arguments,
                    @fileName,
                    @notes,
                    @runAs,
                    @startMode,
                    @idSession
                );
                select last_insert_rowid() from alias;";

            var sql2 = @"insert into alias_name(id_alias, name) values(@idAlias, @name)";
            using (var c = BuildConnection())
            {
                var lastId = c.Query<long>(sql, new { s.Arguments, s.FileName, s.Notes, s.RunAs, s.StartMode, s.IdSession }).FirstOrDefault();

                if (names == null && string.IsNullOrEmpty(s.Name)) { throw new NotSupportedException($"Cannot create a new alias without name."); }
                else if (names == null) { c.Execute(sql2, new { s.Name, IdAlias = lastId }); }
                else
                {
                    foreach (var name in names)
                    {
                        c.Execute(sql2, new { name, IdAlias = lastId });
                    }
                }
            }
        }

        public void Delete(Alias alias)
        {
            var sql = @"delete from alias_name where id_alias = @id";
            var sql2 = @"delete from alias where id = @id";
            using (var c = BuildConnection())
            {
                c.Execute(sql, new { alias.Id });
                c.Execute(sql2, new { alias.Id });
            }
        }

        public void Delete(AliasSession session)
        {
            using (var c = BuildConnection())
            {
                var queries = new string[]
                {
                    @"delete from alias_session where id = @id",
                    @"delete from alias where id_session = @id"
                };
                foreach (var sql in queries) { c.Execute(sql, new { session.Id }); }
            }
        }

        public Alias GetAlias(string name)
        {
            if (_keywordService.IsReserved(name)) { return Alias.Reserved(name); }

            var sql = @"
                select n.Name        as Name
                     , s.id          as Id
                     , s.arguments   as Arguments
                     , s.file_name   as FileName
                     , s.notes       as Notes
                     , s.run_as      as RunAs
                     , s.start_mode  as StartMode
                     , s.working_dir as WorkingDirectory
                from alias s
                inner join alias_name n on s.id = n.id_alias
                where n.name like @name";

            using (var c = BuildConnection())
            {
                var result = c.Query<Alias>(sql, new { name })
                              .FirstOrDefault();
                return result ?? Alias.Empty(name);
            }
        }

        public IEnumerable<Alias> GetAliases(long sessionId)
        {
            var sql = @"
                select n.Name       as Name
                     , s.id         as Id
                     , s.arguments  as Arguments
                     , s.file_name  as FileName
                     , s.notes      as Notes
                     , s.run_as     as RunAs
                     , s.start_mode as StartMode
                     , s.working_dir as WorkingDirectory
                from alias s
                left join alias_name n on s.id = n.id_alias
                where s.id_session = @sessionId
                order by n.name";

            using (var c = BuildConnection())
            {
                var result = c.Query<Alias>(sql, new { sessionId });
                return result ?? new List<Alias>();
            }
        }

        public IEnumerable<AliasText> GetAliasNames(long sessionId)
        {
            var sql = @"
                select
                	sn.Name      as Name,
                	c.exec_count as ExecutionCount,
                	s.file_name  as FileName,
                    'Rocket'     as Kind
                from
                    alias_name sn
                    inner join alias s on s.id = sn.id_alias
                    left join stat_execution_count_v c on c.id_keyword = s.id
                where
                    s.id_session = @sessionId
                order by
                    exec_count desc,
                    name       asc";

            using (var c = BuildConnection())
            {
                var result = c.Query<AliasText>(sql, new { sessionId }).ToList();

                if (result != null)
                {
                    result.AddRange(_reservedKeywordService.GetKeywords());
                    result.AddRange(_pluginManager.GetKeywords());
                }
                else { result = new List<AliasText>(); }

                return result.OrderByDescending(e => e.ExecutionCount)
                             .ThenBy(e => e.Name);
            }
        }

        public IEnumerable<AliasName> GetNamesOf(Alias alias)
        {
            using (var c = BuildConnection())
            {
                var sql = @"
                    select id          as Id
                         , name        as Name
                         , id_alias as IdAlias
                    from alias_name
                    where id_alias = @idAlias";
                var result = c.Query<AliasName>(sql, new { IdAlias = alias.Id });
                return result;
            }
        }

        public AliasSession GetSession(long sessionId)
        {
            var sql = @"
                select id    as id
                     , name  as name
                     , notes as notes
                from alias_session
                where id = @sessionId";
            using (var c = BuildConnection())
            {
                try
                {
                    var result = c.Query<AliasSession>(sql, new { sessionId }).Single();
                    return result;
                }
                catch (InvalidOperationException ex) { throw new InvalidOperationException($"There's no session with ID '{sessionId}'", ex); }
            }
        }

        public AliasSession GetSession(string sessionName)
        {
            var sql = @"
                select
	                id    as id,
	                name  as name,
	                notes as notes
                from alias_session
                where lower(name) = @name";

            using (var db = BuildConnection())
            {
                return db.Query<AliasSession>(sql, new { name = sessionName.ToLower() }).FirstOrDefault();
            }
        }

        public IEnumerable<AliasSession> GetSessions()
        {
            var sql = @"
                select id    as id
                     , name  as name
                     , notes as notes
                from alias_session ";
            using (var c = BuildConnection())
            {
                var result = c.Query<AliasSession>(sql);
                return result.OrderBy(e => e.Name);
            }
        }

        public void SetUsage(Alias alias) => SetUsage(alias.Id);

        public void SetUsage(long idAlias)
        {
            using (var c = BuildConnection())
            {
                var sql = @"insert into alias_usage (id_alias, time_stamp) values (@idAlias, @now)";
                c.Execute(sql, new { idAlias, now = DateTime.Now });
            }
        }

        public void Update(Alias alias)
        {
            alias.Normalise();
            var sql = @"
                update alias
                set
                    arguments   = @arguments,
                    file_name   = @fileName,
                    notes       = @notes,
                    run_as      = @runAs,
                    start_mode  = @startMode,
                    working_dir = @WorkingDirectory
                where id = @id;";
            var sql2 = @"
                update alias_name
                set
                    name = @name
                where id_alias = @id";
            using (var c = BuildConnection())
            {
                c.Execute(sql, new { alias.Arguments, alias.FileName, alias.Notes, alias.RunAs, alias.StartMode, alias.Id, alias.WorkingDirectory });
                c.Execute(sql2, new { alias.Name, alias.Id });
            }
        }

        public void Update(IEnumerable<AliasName> names)
        {
            using (var c = BuildConnection())
            {
                var sqlInsert = @"insert into alias_name (name, id_alias) values (@name, @aliasId)";
                var sqlUpdate = @"update alias_name set name = @name where id = @id";
                foreach (var name in names)
                {
                    if (name.Id == 0)
                    {
                        _log.Debug($"Insert new. id_alias: {name.IdAlias} - name: {name.Name} - id: {name.Id}");
                        c.Execute(sqlInsert, new { name = name.Name, aliasId = name.IdAlias });
                    }
                    else
                    {
                        _log.Debug($"Update. id_alias: {name.IdAlias} - name: {name.Name} - id: {name.Id}");
                        c.Execute(sqlUpdate, new { name.Name, name.Id });
                    }
                }
            }
        }

        public void Update(AliasSession session)
        {
            var sql = @"
                update alias_session
                set
                    name  = @name,
                    notes = @notes
                where id = @id";
            using (var c = BuildConnection())
            {
                c.Execute(sql, new { session.Id, session.Name, session.Notes });
            }
        }

        private DbConnection BuildConnection() => new SQLiteConnection(_connectionString);

        #endregion Methods
    }
}