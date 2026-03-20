using System;
using System.Text;
using System.Threading.Tasks;
using Clienta.Api.Data;
using Microsoft.EntityFrameworkCore;
using Clienta.Api.Services;

namespace Clienta.Api.Services


{
    public class InvoiceService
    {
        private readonly AppDbContext _db;

        public InvoiceService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<byte[]?> GenerateInvoicePdf(Guid id)
        {
            Console.WriteLine($"🔥 PDF REQUEST ID: {id}");

            // ❌ ID לא תקין
            if (id == Guid.Empty)
            {
                Console.WriteLine("❌ Invalid Guid");
                return null;
            }

            // 🔍 חיפוש חשבונית
            var invoice = await _db.Invoices
                .FirstOrDefaultAsync(i => i.Id == id);

            Console.WriteLine($"📦 INVOICE FOUND: {invoice != null}");

            // ❌ לא נמצא → לא זורקים Exception
            if (invoice == null)
            {
                Console.WriteLine($"Invoice NOT FOUND for ID: {id}");
                return null;
            }

            try
            {
                var content = $"Invoice #{invoice.InvoiceNumber}\nClient: {invoice.ClientName}\nAmount: {invoice.Amount}\nDate: {invoice.CreatedAt:dd/MM/yyyy}";
                return Encoding.UTF8.GetBytes(content);
            }
            catch (Exception ex)
            {
                Console.WriteLine("💥 PDF GENERATION ERROR:");
                Console.WriteLine(ex.ToString());

                return null; // לא מפילים שרת
            }
        }
    }
}