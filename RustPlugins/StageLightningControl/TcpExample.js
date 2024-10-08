const net = require("net");
const readline = require("readline");

// Create a TCP client
const client = new net.Socket();

// Connect to the server
client.connect(13377, "XXX.XXX.XXX.XXX", function() {
    console.log("Connected to server");

    // Ask for entity ID after connection is established
    waitForInput();
});

// Listen for data from the server
client.on("data", function(data) {
    console.log("Received: " + data);
});

// Handle connection close event
client.on("close", function() {
    console.log("Connection closed by server");
});

// Handle connection errors
client.on("error", function(err) {
    console.error("Connection error:", err);
});

// Function to handle console input and send data to server
function waitForInput()
{
    const rl = readline.createInterface({
        input: process.stdin,
        output: process.stdout,
    });

    rl.question("Enter the entity ID: ", (input) => {
        if (input.toLowerCase() == "close" && client && client.writable)
        {
            client.end();
            return;
        }
        // Send the entity ID to the server
        if (client && client.writable)
        {
            var times = 0;
            var timeout = setInterval(() => {
                if (times >= 20)
                {
                    console.log("clear interval")
                    clearInterval(timeout);
                }
                console.log("Send interval: " + times);
                client.write(input);
                times++;
            }, 500);
            console.log(`Sent entity ID: ${ input}`);
        } else
        {
            console.log("Client is not connected to the server.");
        }

        // Close the readline interface and wait for more input
        rl.close();
        waitForInput(); // Recursively call to wait for the next input
    });
}
