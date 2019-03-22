using CouchDB.Client;
using CouchDB.Test.Models;
using System.Linq;
using Xunit;

namespace CouchDB.Test
{
    public class ConnectionTest
    {
        [Fact]
        public void CreateConnection()
        {
            using (var connection = new CouchConnection(""))
            {
                QueryProvider provider = new CouchQueryProvider(connection);

                var houses = new Query<House>(provider);

                var query = houses
                    .Where(h =>
                        h.Owner.Name == "Bobby" &&
                        (h.Floors.All(f => f.Area < 120) || h.Floors.Any(f => f.Area > 500))
                    )
                    .OrderByDescending(h => h.Owner.Name)
                    .ThenByDescending(h => h.ConstructionDate)
                    .Skip(0)
                    .Take(50)
                    .Select(h =>
                        new
                        {
                            h.Owner.Name,
                            h.Address,
                            h.ConstructionDate
                        });

                var list = query.ToList();
            }
        }
    }
}