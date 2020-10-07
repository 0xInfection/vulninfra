// Import express
let express = require('express');
// Import Body parser
let bodyParser = require('body-parser');
// Import Mongoose
let mongoose = require('mongoose');
// Initialise the app
const app = express();

// Import routes
let apiRoutes = require("./api-routes");
// Configure bodyparser to handle post requests
app.use(bodyParser.urlencoded({
    extended: true
}));
app.use(bodyParser.json());
// Connect to Mongoose and set connection variable
//need to update this database with mongodb
//mongo "mongodb+srv://resthub.xrdvw.mongodb.net/<dbname>" --username chiayiyou
//mongodb+srv://chiayiyou:<password>@resthub.xrdvw.mongodb.net/<dbname>?retryWrites=true&w=majority
//mongoose.connect('mongodb://localhost/ototb1', { useNewUrlParser: true});
mongoose.connect("mongodb+srv://ch45you:chi45u123@resthub.sssss.mongodb.net/resthub?retryWrites=true&w=majority", { useNewUrlParser: true});
var db = mongoose.connection;

// Added check for DB connection
if(!db)
    console.log("Error connecting db")
else
    console.log("Db connected successfully")

// Setup server port
var port = process.env.PORT || 8080;

// Send message for default URL
app.get('/', (req, res) => res.send('Hello World with Express'));

// Use Api routes in the App
app.use('/api', apiRoutes);
// Launch app to listen to specified port
app.listen(port, function () {
    console.log("Running RestHub on port " + port);
});

module.exports = app;