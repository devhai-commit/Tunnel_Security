using BackendV2.Data;
using BackendV2.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BackendV2.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SensorController : ControllerBase
    {
        private readonly AppDbContext _db;

        public SensorController(AppDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<ActionResult<List<Sensor>>> GetAll()
        {
            var sensors = await _db.Sensors.ToListAsync();
            return Ok(sensors);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Sensor>> GetById(string id)
        {
            var sensor = await _db.Sensors.FindAsync(id);
            if (sensor is null)
            {
                return NotFound();
            }
            return Ok(sensor);
        }

        [HttpPost]
        public async Task<ActionResult<Sensor>> Create(Sensor sensor)
        {
            _db.Sensors.Add(sensor);
            await _db.SaveChangesAsync();
            return CreatedAtAction(nameof(GetById), new { id = sensor.Id }, sensor);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, Sensor sensor)
        {
            if (id != sensor.Id)
            {
                return BadRequest();
            }

            var existing = await _db.Sensors.FindAsync(id);
            if (existing is null)
            {
                return NotFound();
            }

            existing.Id = sensor.Id;
            existing.Name = sensor.Name;
            existing.Type = sensor.Type;
            existing.Description = sensor.Description;

            await _db.SaveChangesAsync();
            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            var existing = await _db.Sensors.FindAsync(id);
            if (existing is null)
            {
                return NotFound();
            }

            _db.Sensors.Remove(existing);
            await _db.SaveChangesAsync();
            return NoContent();
        }
    }
}
