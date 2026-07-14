using BackendV2.Data;
using BackendV2.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BackendV2.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class CameraController : ControllerBase
    {
        private readonly AppDbContext _db;

        public CameraController(AppDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<ActionResult<List<Camera>>> GetAll()
        {
            var cameras = await _db.Cameras.ToListAsync();
            return Ok(cameras);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Camera>> GetById(string id)
        {
            var camera = await _db.Cameras.FindAsync(id);
            if (camera is null)
            {
                return NotFound();
            }
            return Ok(camera);
        }

        [HttpPost]
        public async Task<ActionResult<Camera>> Create(Camera camera)
        {
            _db.Cameras.Add(camera);
            await _db.SaveChangesAsync();
            return CreatedAtAction(nameof(GetById), new { id = camera.Id }, camera);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, Camera camera)
        {
            if (id != camera.Id)
            {
                return BadRequest();
            }

            var existing = await _db.Cameras.FindAsync(id);
            if (existing is null)
            {
                return NotFound();
            }

            existing.Id = camera.Id;
            existing.Name = camera.Name;
            existing.Description = camera.Description;

            await _db.SaveChangesAsync();
            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            var existing = await _db.Cameras.FindAsync(id);
            if (existing is null)
            {
                return NotFound();
            }

            _db.Cameras.Remove(existing);
            await _db.SaveChangesAsync();
            return NoContent();
        }
    }
}
