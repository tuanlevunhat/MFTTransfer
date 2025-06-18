import { useEffect, useState } from 'react';
import Button from 'react-bootstrap/Button';
import Container from 'react-bootstrap/Container';
import Col from 'react-bootstrap/Col';
import Form from 'react-bootstrap/Form';
import Row from 'react-bootstrap/Row';
import api from './services/api';
import { NFTNode } from './enums';
import 'bootstrap/dist/css/bootstrap.min.css';
import { ProgressBar } from 'react-bootstrap';
import { io } from 'socket.io-client';
function App() {
  const [fullPath, setFullPath] = useState('D://testfile.zip');
  const [receivedNodes, setReceivedNodes] = useState([]);
  const [isParallelReceive, setIsParallelReceive] = useState(true);
  const [isTransfering, setIsTransfering] = useState(false);
  const [percentOfNodes, setPercentOfNodes] = useState({});
  
  useEffect(() => {
    const socket = io('http://localhost:3001', {
      transports: ['websocket', 'polling'], // Support both transports
    });
    socket.on('connect', () => {
      console.log('Connected to Socket.IO');
    });
  
    socket.on('statusUpdate', (data) => {
      console.log('Received:', data);
      handleStatusUpdate(data);
    });

    socket.on('disconnect', () => {
        console.log('Disconnected from Socket.IO');
    });

    return () => socket.disconnect();
  }, []);

  useEffect(() => {
    let isAllCompleted = true;
    Object.keys(percentOfNodes).forEach((node) => {
      const progress = percentOfNodes[node]?.progress || 0;
      if (progress < 100) {
        isAllCompleted = false;
      }
    }
    );
    // Check if all nodes have completed their transfer
    // If all nodes are completed, set isTransfering to false
    if(isAllCompleted) {
      setIsTransfering(false);
      console.log('All nodes have completed their transfer');
    }

  }, [percentOfNodes]);

  const handleStatusUpdate = (message) => {
      console.log('Status update received:', message.Status);
      // Handle status updates here, e.g., update UI or state
      setPercentOfNodes((prev) => ({
        ...prev,
        [message.NodeId]: {
          fileId: message.FileId,
          progress: message.Progress,
          status: message.Status
        }
    }));
  };

  const handleReceiveNodesChange = (e) => {
    const { value, checked } = e.target;
    setReceivedNodes((prev) => {
      if (checked) {
        return [...prev, value];
      } else {
        return prev.filter((node) => node !== value);
      }
    });
  }

  const handleFullPathChange = (e) => {
    setFullPath(e.target.value);
  }

  const handleTransfer = async () => {
    try {
      const response = await api.post('FileTransfer/transfer', {
        transferNode: NFTNode.A,
        fullPath: fullPath,
        receivedNodes: receivedNodes,
        isParallelReceive: isParallelReceive
      });
      setIsTransfering(true);
      console.log('Transfer initiated:', response.data);
    } catch (error) {
      console.error('Error during transfer:', error);
      alert('Failed to initiate transfer');
    }
  }

  return (
    <Container fluid="md">
      <Form>
        <Row>
          <Col>
            <Form.Label>MFT Node A</Form.Label>
            <br />
            <Form.Label>{fullPath}</Form.Label>
          </Col>
          <Col>
            {['checkbox'].map((type) => (
              <div key={`reverse-${type}`} className="mb-5">
                <Form.Check
                  label="MFT Node B"
                  name={NFTNode.B}
                  value={NFTNode.B}
                  type={type}
                  id={`reverse-${type}-1`}
                  onChange={handleReceiveNodesChange}
                />
                {isTransfering && <ProgressBar min={0} max={100} now={percentOfNodes[NFTNode.B]?.progress ?? 1} animated='true'></ProgressBar>}
                {!isTransfering && percentOfNodes[NFTNode.B]?.progress === 100 && <span className="text-success">Transfer completed</span>}
                <Form.Check
                  label="MFT Node C"
                  name={NFTNode.C}
                  value={NFTNode.C}
                  type={type}
                  id={`reverse-${type}-2`}
                  onChange={handleReceiveNodesChange}
                />
                {isTransfering && <ProgressBar min={0} max={100} now={percentOfNodes[NFTNode.C]?.progress ?? 1} animated='true'></ProgressBar>}
                {!isTransfering && percentOfNodes[NFTNode.C]?.progress === 100 && <span className="text-success">Transfer completed</span>}
                <Form.Check
                  label="MFT Node D"
                  name={NFTNode.D}
                  value={NFTNode.D}
                  type={type}
                  id={`reverse-${type}-3`}
                  onChange={handleReceiveNodesChange}
                />
                {isTransfering && <ProgressBar min={0} max={100} now={percentOfNodes[NFTNode.D]?.progress ?? 1} animated='true'></ProgressBar>}
                {!isTransfering && percentOfNodes[NFTNode.D]?.progress === 100 && <span className="text-success">Transfer completed</span>}
              </div>
            ))}
          </Col>
        </Row>
        <Button variant="primary" type="button" onClick={handleTransfer}>
          Transfer
        </Button>
      </Form>
    </Container>
  )
}

export default App
