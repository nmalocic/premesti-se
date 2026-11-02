DESCRIPTION:
Idea of this project is to enable parents with young children to match with other parents and switch kindergardens their children are attending.


USER FLOW:
User would open webpage where he can enter their details. FirstName, LastName, Email, PhoneNumber, and pick city based on the city kindergardens are loaded. User can pick one kindergarden as current, and one whre he wants to enrol his child.(Second version should support multuple destination kindergardens).

When submission is sent, we save it as Pending Request, and sent OTP link to provided email for user to accept and confirm, when confirmed it is moved to Request tabel. (Version two think about additional Know Your Customer options for additional security)
Whit each Request we want to check all existing request and check if our source matches someones destination and vice versa. We also want to support graph, where we could combine multiple request (defined by configuration) into graph and connect all those people.
When match is made we want to send email to everyone saying that there is match and they can exchange their details and go to the officials.
We should support interactive map, when the city is selected to show all current Requets to make it easier for people to match with each other.

Arcitecture.
We should use .net core for the backend,
VeloxDB for the databse
React for the frontend


