using Ardalis.GuardClauses;

using FluentValidation;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

using Serilog;

namespace ClaLuLaNa
{
    public class AbstractRepository<TEntity> : UnitOfWork<TEntity> where TEntity : class
    {
        protected AbstractRepository(DbContext context, AbstractValidator<TEntity> validator = null!)
            : base(context, validator)
        {
            /* Nothing more todo */
        }

        public async Task<int> Count(Func<TEntity, bool> predicate, CancellationToken cancellationToken)
        {
            var result = await Task.Run(
                () => _context.Set<TEntity>().Count(predicate), cancellationToken);
            return result;
        }

        public async Task<List<TEntity>> QueryDefault(string sqlQuery, CancellationToken cancellationToken)
        {
            var list = await Task.Run(
                () => _context.Set<TEntity>().FromSqlRaw(sqlQuery), cancellationToken);

            return list.ToList();
        }

        protected async Task<List<TEntity>> ListDefault(CancellationToken cancellationToken)
        {
            // ToList() is needed here
            var result = await Task.Run(
                () => _context.Set<TEntity>().ToList(), cancellationToken);
            Guard.Against.Null(result);
            return result;
        }

        protected async Task<List<TEntity>> ListDefault(Func<TEntity, bool> predicate, CancellationToken cancellationToken)
        {
            // ToList() is needed here
            var result = await Task.Run(
                () => _context.Set<TEntity>().Where(predicate).ToList(), cancellationToken);
            Guard.Against.Null(result);
            return result;
        }

        protected async Task<List<TEntity>> PageDefault(Func<TEntity, bool> predicate, int pageStart, int pageSize, CancellationToken cancellationToken)
        {
            // ToList() is needed here
            var list = await Task.Run(
                () => _context.Set<TEntity>().Where(predicate).ToList(), cancellationToken);

            // First test: The page is inside the list
            if (list.Count > pageStart + pageSize)
                return list.GetRange(pageStart, pageSize);
            // Second test: The list is smaller than page
            if (list.Count <= pageSize || pageSize == -1)
                return list;
            // Third test: The page is the end of the list
            return list.GetRange(pageStart, list.Count - pageStart);
        }

        protected async Task<List<T>> SelectDefault<T>(Func<TEntity, T> converter, CancellationToken cancellationToken)
        {
            // ToList() is needed here
            var result = await Task.Run(
                () => _context.Set<TEntity>().Select(converter).ToList(), cancellationToken);
            Guard.Against.Null(result);
            return result;
        }

        protected async Task<List<T>> SelectDefault<T>(Func<TEntity, bool> predicate1, Func<TEntity, T> converter, CancellationToken cancellationToken)
        {
            // ToList() is needed here
            var result = await Task.Run(
                () => _context.Set<TEntity>().Where(predicate1).Select(converter).ToList(), cancellationToken);
            Guard.Against.Null(result);
            return result;
        }

        protected async Task<TEntity?> ReadDefault(Func<TEntity, bool> predicate, CancellationToken cancellationToken)
        {
            //var result = _context.Set<TEntity>().FirstOrDefault(predicate);
            var result = await Task.Run(
                () => _context.Set<TEntity>().FirstOrDefault(predicate), cancellationToken);
            return result;
        }

        protected async Task<TEntity?> CreateDefault(TEntity entity, CancellationToken cancellationToken)
        {
            if (!ValidateModel(entity))
                return null;

            try
            {
                var result = _context.Set<TEntity>().Add(entity);
                if (result is null)
                    return null;

                await SaveChangesAsyncAndNotify(cancellationToken);

                return result.Entity;
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"{nameof(CreateDefault)}({nameof(TEntity)})");
            }

            return null;
        }

        protected async Task<TEntity?> UpdateDefault(TEntity entity, CancellationToken cancellationToken)
        {
            if (!ValidateModel(entity))
                return null;

            bool saveFailed;

            do
            {
                saveFailed = false;

                try
                {
                    EntityEntry<TEntity> result = _context.Set<TEntity>().Update(entity);
                    if (result is null)
                        return null;

                    await SaveChangesAsyncAndNotify(cancellationToken);

                    return result.Entity;
                }
                catch (DbUpdateConcurrencyException ex)
                {
                    /*************************************************************************/
                    /* Code from https://learn.microsoft.com/pt-br/ef/ef6/saving/concurrency */
                    /*************************************************************************/

                    saveFailed = true;

                    // Get the current entity values and the values in the database
                    // as instances of the entity type
                    var entry = ex.Entries.Single();
                    var databaseValues = entry.GetDatabaseValues();
                    Guard.Against.Null(databaseValues);
                    var databaseValuesAsT = (TEntity)databaseValues.ToObject();

                    // Choose an initial set of resolved values. In this case we
                    // make the default be the values currently in the database.
                    var resolvedValuesAsT = (TEntity)databaseValues.ToObject();

                    // Have the user choose what the resolved values should be
                    HaveUserResolveConcurrency((TEntity)entry.Entity, databaseValuesAsT, resolvedValuesAsT);

                    // Update the original values with the database values and
                    // the current values with whatever the user choose.
                    entry.OriginalValues.SetValues(databaseValues);
                    entry.CurrentValues.SetValues(resolvedValuesAsT);
                }
                catch (Exception ex)
                {
                    Log.Fatal(ex, $"{nameof(UpdateDefault)}({nameof(TEntity)})");
                }
            }
            while (saveFailed);

            return null;
        }

        protected async Task<TEntity?> DeleteDefault(TEntity entity, CancellationToken cancellationToken)
        {
            if (!ValidateModel(entity))
                return null;

            try
            {
                var deleted = _context.Set<TEntity>().Remove(entity);
                if (deleted is null)
                    return null;

                await SaveChangesAsyncAndNotify(cancellationToken);

                return deleted.Entity;
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, $"{nameof(DeleteDefault)}({nameof(TEntity)})");
            }

            return null;
        }

        protected void DetachLocal(Func<TEntity, bool> predicate)
        {
            var local = _context.Set<TEntity>().Local.Where(predicate).FirstOrDefault();
            if (local is not null)
                _context.Entry(local).State = EntityState.Detached;
        }

        public int ExecuteSqlRaw(string sql, params object[] parameters)
        {
            int rows = 0;
            var transaction = _context.Database.BeginTransaction(System.Data.IsolationLevel.Serializable);

            try
            {
                rows = _context.Database.ExecuteSqlRaw(sql, parameters);
                // Commit transaction if all commands succeed, transaction will auto-rollback
                // when disposed if either commands fails
                transaction.Commit();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            return rows;
        }
    }
}