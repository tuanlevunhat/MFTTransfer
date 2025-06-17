// server.js
import { Kafka } from 'kafkajs';
import { Server } from 'socket.io';
import { createServer } from 'http';
const brokers = ['127.0.0.1:9093'];
console.log('🧪 Kafka brokers used:', brokers);
const kafka = new Kafka({ 
    clientId: 'mft-app', // Match logs
    brokers:brokers,
    sessionTimeout: 3600000, // 1 hour
    maxPollIntervalMs: 3600000, // 1 hour 
  });
const consumer = kafka.consumer({ groupId: 'mft-group' });

const httpServer = createServer();
const io = new Server(httpServer, {
  cors: {
    origin: 'http://localhost:5173',
    methods: ['GET', 'POST'],
  },
});

io.on('connection', (socket) => {
  console.log('Client connected:', socket.id);
  socket.on('disconnect', () => {
    console.log('Client disconnected:', socket.id);
  });
});

httpServer.listen(3001, () => {
  console.log('WebSocket server listening on http://localhost:3001');
});

(async () => {
  try {
    await consumer.connect();
    console.log('Connected to Kafka broker');
    await consumer.subscribe({ topic: 'transfer-progress', fromBeginning: true });
    console.log('Subscribed to transfer-progress topic');

    await consumer.run({
      eachMessage: async ({ message }) => {
        try {
          const value = message.value.toString();
          console.log('Kafka Message:', value);
          io.emit('statusUpdate', JSON.parse(value));
        } catch (error) {
          console.error('Error processing Kafka message:', error.message);
        }
      },
    });
  } catch (error) {
    console.error('Kafka consumer error:', error.message);
    // Don't exit; keep server running
  }
})();

process.on('SIGTERM', async () => {
  console.log('Shutting down...');
  await consumer.disconnect();
  httpServer.close();
  process.exit(0);
});