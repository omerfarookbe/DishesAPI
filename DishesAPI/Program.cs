using AutoMapper;
using DishesAPI.DbContexts;
using DishesAPI.Entities;
using DishesAPI.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// register the DbContext on the container, getting the
// connection string from appSettings   
builder.Services.AddDbContext<DishesDbContext>(o => o.UseSqlite(
    builder.Configuration["ConnectionStrings:DishesDBConnectionString"]));

builder.Services.AddAutoMapper(AppDomain.CurrentDomain.GetAssemblies());

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler();
    app.UseExceptionHandler(configureApplicationBuilder =>
    {
        configureApplicationBuilder.Run(
            async context =>
            {
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                context.Response.ContentType = "text/html";
                await context.Response.WriteAsync("An unexpected problem happened.");
            });
    });
}

// Configure the HTTP request pipeline.

app.UseHttpsRedirection();

var dishesEndpoints = app.MapGroup("/dishes");
var dishWithGuidIdEndpoints = dishesEndpoints.MapGroup("/{dishId:guid}");
var ingredientsEndpoints = dishWithGuidIdEndpoints.MapGroup("/ingredients");

dishesEndpoints.MapGet("", async Task<Ok<IEnumerable<DishDto>>> (DishesDbContext dishesDbContext,
    ClaimsPrincipal claimsPrincipal,
    IMapper mapper,
    string? name) =>
{
    Console.WriteLine($"User authenticated? {claimsPrincipal.Identity?.IsAuthenticated}");

    return TypedResults.Ok(mapper.Map<IEnumerable<DishDto>>(await dishesDbContext.Dishes
        .Where(d => name == null || d.Name.Contains(name))
        .ToListAsync()));
});

dishWithGuidIdEndpoints.MapGet("", async Task<Results<NotFound, Ok<DishDto>>> (DishesDbContext dishesDbContext, 
    IMapper mapper,
    Guid dishId) => 
{
    var dishEntity = await dishesDbContext.Dishes
        .FirstOrDefaultAsync(d => d.Id == dishId);
    if (dishEntity == null)
    {
        return TypedResults.NotFound();
    }

    return TypedResults.Ok(mapper.Map<DishDto>(dishEntity));
}).WithName("GetDish");

dishesEndpoints.MapGet("/{dishName}", async Task<Ok<DishDto>> (DishesDbContext dishesDbContext,
    IMapper mapper,
    string dishName) =>
{
    return TypedResults.Ok(mapper.Map<DishDto>(await dishesDbContext.Dishes
        .FirstOrDefaultAsync(d => d.Name == dishName)));
});

ingredientsEndpoints.MapGet("", async
    Task<Results<NotFound, Ok<IEnumerable<IngredientDto>>>> (DishesDbContext dishesDbContext,
    IMapper mapper,
    Guid dishId) =>
{
    var dishEntity = await dishesDbContext.Dishes
        .FirstOrDefaultAsync(d => d.Id == dishId);
    if (dishEntity == null)
    {
        return TypedResults.NotFound();
    }

    return TypedResults.Ok(mapper.Map<IEnumerable<IngredientDto>>((await dishesDbContext.Dishes
        .Include(d => d.Ingredients)
        .FirstOrDefaultAsync(d => d.Id == dishId))?.Ingredients));
});

dishesEndpoints.MapPost("", async Task<CreatedAtRoute<DishDto>> (DishesDbContext dishesDbContext,
    IMapper mapper,  
    DishForCreationDto dishForCreationDto
    //LinkGenerator linkGenerator,
    //HttpContext httpContext
) =>
{
    var dishEntity = mapper.Map<Dish>(dishForCreationDto);
    dishesDbContext.Add(dishEntity);
    await dishesDbContext.SaveChangesAsync();

    var dishToReturn = mapper.Map<DishDto>(dishEntity);

    return TypedResults.CreatedAtRoute(
        dishToReturn,
        "GetDish",
        new { dishId = dishToReturn.Id });
});

dishWithGuidIdEndpoints.MapPut("", async Task<Results<NotFound, NoContent>> (DishesDbContext dishesDbContext,
    IMapper mapper,
    Guid dishId,
    DishForUpdateDto dishForUpdateDto) =>
{
    var dishEntity = await dishesDbContext.Dishes
        .FirstOrDefaultAsync(d => d.Id == dishId);
    if (dishEntity == null)
    {
        return TypedResults.NotFound();
    }

    mapper.Map(dishForUpdateDto, dishEntity);

    await dishesDbContext.SaveChangesAsync();

    return TypedResults.NoContent();
});

dishWithGuidIdEndpoints.MapDelete("", async Task<Results<NotFound, NoContent>> (DishesDbContext dishesDbContext,
    Guid dishId) =>
{
    var dishEntity = await dishesDbContext.Dishes
        .FirstOrDefaultAsync(d => d.Id == dishId);
    if (dishEntity == null)
    {
        return TypedResults.NotFound();
    }

    dishesDbContext.Dishes.Remove(dishEntity);
    await dishesDbContext.SaveChangesAsync();
    return TypedResults.NoContent();
});


// recreate & migrate the database on each run, for demo purposes
using (var serviceScope = app.Services.GetService<IServiceScopeFactory>().CreateScope())
{
    var context = serviceScope.ServiceProvider.GetRequiredService<DishesDbContext>();
    context.Database.EnsureDeleted();
    context.Database.Migrate();
}


app.Run();