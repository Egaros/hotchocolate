using System.Linq.Expressions;
using HotChocolate.Language;

namespace HotChocolate.Data.Filters.Expressions
{
    public class QueryableStringNotEqualsHandler : QueryableStringOperationHandler
    {
        protected override int Operation => DefaultOperations.NotEquals;

        public override Expression HandleOperation(
            QueryableFilterContext context,
            IFilterOperationField field,
            IValueNode value,
            object parsedValue)
        {
            Expression property = context.GetInstance();
            return FilterExpressionBuilder.NotEquals(property, parsedValue);
        }
    }
}
