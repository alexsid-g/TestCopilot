using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.AspNetCore.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Use of ConcurrentDictionary suggested by Copilot
var users = new ConcurrentDictionary<int, User>(){
    [1] = new User { Id = 1, Name = "User Name 1", Email = "user@mail.com"},
    [2] = new User { Id = 2, Name = "Alex Sid 2", Email = "alexsid@mail.com"},
};
var nextId = 3;

// Add middleware to catch unhandled exceptions
app.Map("/error", (HttpContext ctx) => {
    var exception = ctx.Features
        .Get<IExceptionHandlerFeature>()?
        .Error;
    return Results.Problem(exception?.Message);
});
app.UseHttpsRedirection();

// Middleware to handle exceptions
app.Use(async (context, next) =>
{
    Console.WriteLine("* Error Handling Middleware");
    try
    {
        await next(); // Call the next middleware
    }
    catch (Exception ex)
    {
        // Log the exception (optional)
        Console.WriteLine($"Unhandled Exception: {ex.Message}");

        // Set the response status code and content type
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";

        // Create a JSON response
        var errorResponse = new
        {
            error = $"Internal server error: {ex.Message}."
        };

        // Write the JSON response
        await context.Response.WriteAsJsonAsync(errorResponse);
    }
});

// Middlware for token-based authentication
app.Use(async (context, next) =>
{
    Console.WriteLine("* Token-based Authentication Middleware");

    // Check if the Authorization header is present
    if (!context.Request.Headers.TryGetValue("Authorization", out var token))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "Authorization token is missing." });
        return;
    }

    // Validate the token (replace this with your actual token validation logic)
    const string validToken = "your-secret-token"; // Replace with your actual token
    if (token != $"Bearer {validToken}")
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new { error = "Invalid or expired token." });
        return;
    }

    // If the token is valid, proceed to the next middleware
    await next();
});

// Middleware to log requests and responses
app.Use(async (context, next) =>
{
    Console.WriteLine("* Logging Middleware: Request and Response");
    // Log the HTTP Request
    Console.WriteLine($"Incoming Request: {context.Request.Method} {context.Request.Path}");
    if (context.Request.ContentLength > 0 && context.Request.Body.CanSeek)
    {
        // Debugged with Copilot (added EnableBuffering)
        context.Request.EnableBuffering();
        context.Request.Body.Position = 0;

        using var reader = new StreamReader(context.Request.Body);
        var body = await reader.ReadToEndAsync();
        Console.WriteLine($"Request Body: {body}");
        context.Request.Body.Position = 0; // Reset the stream position
    }

    // Capture the HTTP Response
    var originalBodyStream = context.Response.Body;
    using var responseBody = new MemoryStream();
    context.Response.Body = responseBody;

    try
    {
        await next(); // Call the next middleware

        // Log the HTTP Response
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var responseText = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Console.WriteLine($"Response: {context.Response.StatusCode} {responseText}");
        context.Response.Body.Seek(0, SeekOrigin.Begin);

        await responseBody.CopyToAsync(originalBodyStream); // Copy the response back to the original stream
    }
    finally
    {
        context.Response.Body = originalBodyStream; // Restore the original response stream
    }
});

// Minimal API endpoints for Users
app.MapGet("/users", () =>
{
    return Results.Ok(users.Values);
});

app.MapGet("/users/{id:int}", (int id) =>
{
    if (users.TryGetValue(id, out var user))
    {
        return Results.Ok(user);
    }
    return Results.NotFound();
});

// Validation added by Copilot
app.MapPost("/users", (User user) =>
{
    if (string.IsNullOrWhiteSpace(user.Name) ||
        string.IsNullOrWhiteSpace(user.Email))
    {
        return Results.BadRequest("Name and Email are required.");
    }

    user.Id = Interlocked.Increment(ref nextId);
    users.TryAdd(user.Id, user);
    return Results.Created($"/users/{user.Id}", user);
});

// Validation added by Copilot
app.MapPut("/users/{id:int}", (int id, User updatedUser) =>
{
    if (id != updatedUser.Id)
    {
        return Results.BadRequest("ID in URL does not match ID in the body.");
    }

    if (string.IsNullOrWhiteSpace(updatedUser.Name) ||
        string.IsNullOrWhiteSpace(updatedUser.Email))
    {
        return Results.BadRequest("Name and Email are required.");
    }

    if (users.ContainsKey(id))
    {
        updatedUser.Id = id;
        users[id] = updatedUser;
        return Results.Ok(updatedUser);
    }
    return Results.NotFound();
});

app.MapDelete("/users/{id:int}", (int id) =>
{
    if (users.TryRemove(id, out var _))
    {
        return Results.NoContent();
    }
    return Results.NotFound();
});

app.MapGet("/exception", () =>
{
    throw new Exception("This is a test exception.");
});

app.MapControllers();
if (app.Environment.IsDevelopment())
{
    // NOTE: this middleware blocks our's (comment it out)
    //app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "My API V1");
        c.RoutePrefix = string.Empty; // Set Swagger UI at the app's root
    });
}
else
{
    app.UseHsts();
}
app.Run();

///////////////////////////////////////////////////////////////////////////
// User class definition
record User
{
    public int Id { get; set; }
    required public string Name { get; set; }
    required public string Email { get; set; }
}
