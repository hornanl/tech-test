using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Order.Model.Exceptions;

namespace OrderService.WebAPI.ExceptionHandlers
{
    public class OrderDataExceptionHandler : IExceptionHandler
    {
        private readonly ILogger<OrderDataExceptionHandler> _logger;
        private readonly IProblemDetailsService _problemDetailsService;

        public OrderDataExceptionHandler(
            ILogger<OrderDataExceptionHandler> logger,
            IProblemDetailsService problemDetailsService)
        {
            _logger = logger;
            _problemDetailsService = problemDetailsService;
        }

        public async ValueTask<bool> TryHandleAsync(
            HttpContext httpContext,
            Exception exception,
            CancellationToken cancellationToken)
        {
            // Handle OrderDataException only. Let the middleware handle other errors.
            if (exception is not OrderDataException)
            {
                return false;
            }

            // The client cannot fix this error by changing the request.
            // Log it and return a 500 response with the problem details.
            _logger.LogError(exception, "Order data is invalid.");
            httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

            await _problemDetailsService.WriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Title = "Order data is invalid",
                    Detail = exception.Message
                }
            });

            return true;
        }
    }
}
