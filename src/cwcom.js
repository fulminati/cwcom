
var prompt = require("prompt");

prompt.start();

prompt.get(["username", "email"], function (err, result) {
    console.log('Command-line input received:');
    console.log('  username: ' + result.username);
    console.log('  email: ' + result.email);
});

/*
var host = "morsecode.dyndns.org";
var c_port = 7890;
var dgram = require("dgram");
var client = dgram.createSocket("udp4");


var ID = new Buffer(496);

ID[0] = 3;
ID[1] = 0;
ID[2] = 0xEC;
ID[3] = 1;
ID[4] = 1;
ID[8] = 67;

console.log(ID)

var message = new Buffer("3031323334353637", "hex");

client.on("message", function(msg, rinfo) {
  console.log("recieved: " + msg.toString("hex"));
  send(ID, host, c_port);
});

client.on("err", function(err) {
  console.log("client error: \n" + err.stack);
  client.close();
});

client.on("close", function() {
  console.log("closed.");
});

send(Buffer.from([0x04, 0x00, 0x96, 0x00]), host, c_port);
function send(message, host, port) {
  client.send(message, 0, message.length, port, host, function(err, bytes) {
    console.log("sent.");
  });
}

setTimeout(function(){
    client.close();
}, 5000);

*/