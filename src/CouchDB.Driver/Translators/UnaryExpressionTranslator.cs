﻿using CouchDB.Driver.Extensions;
using System;
using System.Linq.Expressions;

namespace CouchDB.Driver
{
    internal partial class QueryTranslator
    {
        protected override Expression VisitUnary(UnaryExpression u)
        {
            switch (u.NodeType)
            {
                case ExpressionType.Not:
                    if (u.Operand is BinaryExpression b && (b.NodeType == ExpressionType.Or || b.NodeType == ExpressionType.OrElse))
                    {
                        _sb.Append("{");
                        VisitBinaryCombinationOperator(b, true);
                        _sb.Append("}");
                    }
                    else if (u.Operand is MethodCallExpression m && m.Method.Name == "In")
                    {
                        VisitInMethod(m, true);
                    }
                    else
                    {
                        _sb.Append("{");
                        _sb.Append("\"$not\":");
                        Visit(u.Operand);
                        _sb.Append("}");
                    }
                    break;
                case ExpressionType.Convert:
                    Visit(u.Operand);
                    break;
                default:
                    throw new NotSupportedException(string.Format("The unary operator '{0}' is not supported", u.NodeType));
            }
            return u;
        }

    }
}
