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
    public class NodeController : ControllerBase
    {
        private readonly AppDbContext _db;

        public NodeController(AppDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<ActionResult<List<Node>>> GetAll()
        {
            var nodes = await _db.Nodes.ToListAsync();
            return Ok(nodes);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Node>> GetById(string id)
        {
            var node = await _db.Nodes.FindAsync(id);
            if (node is null)
            {
                return NotFound();
            }
            return Ok(node);
        }

        [HttpPost]
        public async Task<ActionResult<Node>> Create(Node node)
        {
            _db.Nodes.Add(node);
            await _db.SaveChangesAsync();
            return CreatedAtAction(nameof(GetById), new { id = node.Id }, node);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, Node node)
        {
            if (id != node.Id)
            {
                return BadRequest();
            }

            var existing = await _db.Nodes.FindAsync(id);
            if (existing is null)
            {
                return NotFound();
            }

            existing.Name = node.Name;
            existing.Latitude = node.Latitude;
            existing.Longitude = node.Longitude;
            existing.Description = node.Description;

            await _db.SaveChangesAsync();
            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            var existing = await _db.Nodes.FindAsync(id);
            if (existing is null)
            {
                return NotFound();
            }

            _db.Nodes.Remove(existing);
            await _db.SaveChangesAsync();
            return NoContent();
        }
    }
}
