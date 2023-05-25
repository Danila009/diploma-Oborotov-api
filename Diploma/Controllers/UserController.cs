﻿using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Diploma.Auth;
using Diploma.Database;
using Diploma.model.user;
using Diploma.Repository;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Diploma.Controllers;

[Route("api/[controller]")]
[ApiController]
public class UserController : ControllerBase
{
    private EfModel _efModel;
    private FileRepository imageRepository = new FileRepository();

    public UserController(EfModel model)
    {
        _efModel = model;
    }

    [Authorize]
    [HttpPut]
    public async Task<ActionResult<User>> UpdateUser(UpdateUserDto dto)
    {
        if (HttpContext.User.Identity is not ClaimsIdentity identity)
            return NotFound();

        var id = Convert.ToInt32(identity.FindFirst("Id")?.Value);

        var user = await _efModel.Users.FindAsync(id);

        if (user == null)
            return NotFound();

        user.Login = dto.Login;
        user.FirstName = dto.FirstName;
        user.LastName = dto.LastName;
        user.MidleName = dto.MidleName;

        _efModel.Entry(user).State = EntityState.Modified;
        await _efModel.SaveChangesAsync();

        return user;
    }

    //[Authorize(Roles = "AdminUser")]
    [HttpGet("/api/Users")]
    public async Task<ActionResult<List<User>>> GetUsers(string? search, string? role)
    {
        IQueryable<User> users = _efModel.Users;

        if(search != null)
        {
            users = users.Where(u =>
                u.FirstName.Contains(search)
                || u.MidleName.Contains(search) || u.LastName.Contains(search));
        }

        if(role != null)
        {
            users = users.Where(u => u.Role == role);
        }

        return await users.ToListAsync();
    }

    [Authorize]
    [HttpGet]
    public async Task<ActionResult<User>> Get()
    {
        if (HttpContext.User.Identity is not ClaimsIdentity identity)
            return NotFound();
            
        var id = Convert.ToInt32(identity.FindFirst("Id")?.Value);

        var user = await _efModel.Users.FirstOrDefaultAsync(u => u.Id == id);

        return Ok(user);
    }

    [Authorize]
    [HttpPatch("Photo")]
    public async Task<ActionResult> UpdatePhoto(IFormFile file)
    {
        if (HttpContext.User.Identity is not ClaimsIdentity identity)
            return NotFound();

        var id = Convert.ToInt32(identity.FindFirst("Id")?.Value);

        var user = await _efModel.Users.FirstOrDefaultAsync(u => u.Id == id);

        if (user == null)
            return NoContent();

        var uri = await imageRepository.UploadFile(
            path: $"resources/users/{id}/photo/",
            fileId: new Random().Next(0, int.MaxValue).ToString(),
            file: file
        );

        var url = $"http://localhost:5000/api/User/Photo.jpg?uri={uri}";

        user.Photo = url;

        _efModel.Entry(user).State = EntityState.Modified;

        await _efModel.SaveChangesAsync();

        return Ok(url);
    }

    [HttpGet("Photo.jpg")]
    public ActionResult GetUserPhoto(string uri)
    {
        byte[]? file = imageRepository.GetFile(
            uri
        );

        if (file != null)
            return File(file, "image/jpeg");
        else
            return NotFound();
    }

    [HttpPost("/api/Registration")]
    public async Task<ActionResult> PostRegistration(RegistrationDTO userDTO)
    {
        if (userDTO == null)
            return BadRequest();

        _efModel.Users.Add(new User
        {
            Password = userDTO.Password,
            Login = userDTO.Login,
            FirstName = userDTO.FirstName,
            LastName = userDTO.LastName,
            MidleName = userDTO.MidleName
        });

        await _efModel.SaveChangesAsync();

        return Ok();
    }

    [HttpPost("/api/Authorization")]
    public ActionResult<object> Token(AuthorizationDTO authorization)
    {
        var indentity = GetIdentity(authorization.Login, authorization.Password);

        if (indentity == null)
        {
            return BadRequest();
        }

        var now = DateTime.UtcNow;
        var jwt = new JwtSecurityToken(
            audience: TokenBaseOptions.AUDIENCE,
            issuer: TokenBaseOptions.ISSUER,
            notBefore: now,
            claims: indentity.Claims,
            expires: now.Add(TimeSpan.FromDays(TokenBaseOptions.LIFETIME)),
            signingCredentials: new SigningCredentials(TokenBaseOptions.GetSymmetricSecurityKey(),
                SecurityAlgorithms.HmacSha256));

        var encodedJwt = new JwtSecurityTokenHandler().WriteToken(jwt);

        var response = new
        {
            access_token = encodedJwt,
            username = indentity.Name,
            role = indentity.FindFirst(ClaimsIdentity.DefaultRoleClaimType).Value
        };

        return response;
    }
    
    [NonAction]
    public ClaimsIdentity? GetIdentity(string login, string password)
    {
        var user = _efModel.Users.FirstOrDefault(
            x => x != null && x.Login == login && x.Password == password);

        if (user != null)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimsIdentity.DefaultNameClaimType, user.FirstName),
                new Claim(ClaimsIdentity.DefaultRoleClaimType, user.Role),
                new Claim("Id", user.Id.ToString())
            };

            ClaimsIdentity claimsIdentity =
                new ClaimsIdentity(claims, "Token", ClaimsIdentity.DefaultNameClaimType,
                    ClaimsIdentity.DefaultRoleClaimType);

            return claimsIdentity;
        }
        
        return null;
    }
}