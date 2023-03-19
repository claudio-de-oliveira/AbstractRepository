using FluentValidation;

using Microsoft.EntityFrameworkCore;

using Serilog;

using System.Reflection;

namespace ClaLuLaNa
{
    public class UnitOfWork<TEntity>
    {
        protected readonly DbContext _context;
        protected readonly AbstractValidator<TEntity> _validator;

        protected UnitOfWork(DbContext context, AbstractValidator<TEntity> validator = null!)
        {
            _context = context;
            _validator = validator;
        }

        public Task SaveChangesAsyncAndNotify(CancellationToken cancellationToken = default)
        {
            // ConvertDomainEventsToOutboxMessages();
            // UpdateAuditableEntities();

            return _context.SaveChangesAsync(cancellationToken);
        }

        protected bool ValidateModel(TEntity entity)
        {
            try
            {
                if (entity is null)
                    throw new ArgumentNullException(nameof(entity));

                if (_validator is not null)
                {
                    var validationResult = _validator.Validate(entity);
                    if (!validationResult.IsValid)
                        return false;
                }
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, $"{nameof(ValidateModel)}({nameof(TEntity)})");
            }

            return true;
        }

        protected virtual void HaveUserResolveConcurrency(TEntity currentValues, TEntity databaseValues, TEntity resolvedValues)
        {
            // Show the current, database, and resolved values to the user and have
            // them edit the resolved values to get the correct resolution.

            PropertyInfo[] props = typeof(TEntity).GetProperties();

            foreach (var prop in props)
            {
                if (prop.GetIndexParameters().Length == 0)
                {
                    var currentValue = prop.GetValue(currentValues);
                    var databaseValue = prop.GetValue(databaseValues);

                    prop.SetValue(resolvedValues, currentValue);

                    var resolvedValue = prop.GetValue(resolvedValues);

                    if (currentValue is not null && !currentValue.Equals(databaseValue))
                    {
                        // resolvedValue = currentValue;
                        Log.Error(
                            $"Conflito de simultaneidade (EF6) com {prop.Name}\n" +
                            $"\t- Banco de dados contém {databaseValue}\n" +
                            $"\t- Novo valor desejável: {currentValue}\n" +
                            $"\t=======> Valor gravado: {resolvedValue}");
                    }

                    prop.SetValue(resolvedValues, currentValue);
                }
            }
        }
    }
}
