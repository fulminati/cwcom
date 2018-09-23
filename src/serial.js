var SerialPort = require('serialport');
var port = new SerialPort('/dev/ttyUSB0', {
    baudRate: 9600
});

// Switches the port into "flowing mode"
port.on('data', function (data) {
    console.log('Data:', data+"");
});