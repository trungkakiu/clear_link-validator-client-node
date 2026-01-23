using Microsoft.AspNetCore.Http;
using Microsoft.Identity.Client.Extensions.Msal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using WorkerService1.Controller;

namespace WorkerService1.Services.Middle_ware;

public class ValidatorMiddleware
{
    private readonly NodeDatabase _db;

    public ValidatorMiddleware(NodeDatabase db)
    {
        _db = db;
    }
    public async Task<IResult> CheckNodeActive(HttpContext context, Func<Task<IResult>> next)
    {
        var status = _db.GetStatus();

        if (status != "active")
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        return await next();
    }
}
