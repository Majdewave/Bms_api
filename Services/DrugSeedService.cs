using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Clienta.Api.Data;
using Clienta.Api.Entities;

namespace Clienta.Api.Services
{
    public class DrugSeedService
    {
        private readonly AppDbContext _context;
        public DrugSeedService(AppDbContext context)
        {
            _context = context;
        }

        public async Task SeedAsync()
        {
            var drugs = new List<(string Name, string? Dosage)>
            {
                ("Telfast", "180mg"),
                ("Telfast", "120mg"),
                ("Abitren", "100mg"),
                ("Augmentin", "875mg"),
                ("Flagyl", "500mg"),
                ("Flagyl", "250mg"),
                ("Azemil", "250mg"),
                ("Fluconazole", "150mg"),
                ("Vermox", "10mg"),
                ("Dalacin", "300mg"),
                ("Cerazette", null),
                ("Zoely", null),
                ("Yasmin", null),
                ("Yasmin Plus", null),
                ("Yaz Plus", null),
                ("Melian", null),
                ("Progyluton", null),
                ("Feminet", null),
                ("Provera", "5mg"),
                ("Duphaston", "10mg"),
                ("Famotidine", "20mg"),
                ("Betmiga", "50mg"),
                ("Letrozole", "2.5mg"),
                ("Dexamethasone", "0.5mg"),
                ("Dexamethasone", "2mg"),
                ("Monocyclin", "300mg"),
                ("Acyclovir", "400mg"),
                ("Macrodantin", "100mg"),
                ("Doxycyclin", "100mg"),
                ("Valtrex", null),
                ("Loratadine", null),
                ("Paracetamol", "500mg"),
                ("Zofran", null),
                ("Bonjesta", null),
                ("Losec", "20mg"),
                ("Aspirin", "100mg"),
                ("Optalgin", "500mg"),
                ("Prednisone", "20mg"),
                ("Endometrin", "100mg"),
                ("Utrogestan", "200mg"),
                ("Proluton", "250mg"),
                ("Ovestin", "10mg"),
                ("Intrarosa", "0.5mg"),
                ("Dalacin Vaginal", null),
                ("Clexane", null),
                ("Ovitrelle", "250mcg"),
                ("Evra Patch", null),
                ("NuvaRing", null),
                ("Mirena", null),
                ("Kyleena", null),
                ("Nova T", null)
            };

            foreach (var drug in drugs)
            {
                var exists = await _context.Drugs
                    .AnyAsync(d => d.Name == drug.Name && d.Dosage == drug.Dosage);
                if (!exists)
                {
                    _context.Drugs.Add(new Drug
                    {
                        Id = Guid.NewGuid(),
                        Name = drug.Name,
                        Dosage = drug.Dosage,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            await _context.SaveChangesAsync();
        }
    }
}
