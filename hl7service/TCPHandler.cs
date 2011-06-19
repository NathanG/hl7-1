using System;
using System.Text;
using System.Net.Sockets;
using System.Threading;
using System.Net;
using System.IO;

namespace hl7service
{
	public class TCPHandler
	{
		private static char END_OF_BLOCK = (char)0x001c;
		private static char START_OF_BLOCK = (char)0x000b;
		private static char CARRIAGE_RETURN = (char)13;
		private static char FIELD_DELIMITER = '|';
		private static int MESSAGE_CONTROL_ID_LOCATION = 9;
		protected string PATIENT_ID = "PID";
		
    	private Thread listenThread;

		private TcpListener tcpListener;
		
		private string folder;

        public TCPHandler(int port, string folder)
        {
			this.folder = folder;
			
			Logger.Debug("Starting TCP listener server at port " + port.ToString());
			Logger.Debug("TCP server will save messages in '" + folder + "' folder.");
			
            this.tcpListener = new TcpListener(IPAddress.Any, port);
            
			this.listenThread = new Thread(new ThreadStart(ListenForClients));
            this.listenThread.Start();
        }
        
        private void ListenForClients()
        {
            this.tcpListener.Start();

            while (true)
            {
                // blocks until a client has connected to the server
                TcpClient client = this.tcpListener.AcceptTcpClient();

                // create a thread to handle communication 
                // with connected client
				
                Thread clientThread = new Thread(new ParameterizedThreadStart(HandleClientComm));
                clientThread.Start(client);
            }
        }
		
		private string getMessageControlId(string message)
		{
			// get Message Control Id from a v2 hl7 message
			
        	// Split string
        	string[] fields = message.Split(FIELD_DELIMITER);
			
			return fields[MESSAGE_CONTROL_ID_LOCATION-1];
		}
		
		private void HandleClientComm(object client)
		{		
			TcpClient tcpClient = (TcpClient)client;
			NetworkStream clientStream = tcpClient.GetStream();
			
			byte[] message = new byte[8192];
			int bytesRead;
						                 
			while (true)
			{
				bool writeToDisk = false;
				bool hl7v2 = false;
				bool hl7v3 = false;

				bytesRead = 0;
				
				try
				{
					bytesRead = clientStream.Read(message, 0, 4096);
				}
				catch(Exception e)
				{
					// A socket error has occured
					Logger.Fatal(e.Message);
					break;
				}
				
				if (bytesRead == 0)
				{
					Logger.Debug("A client has disconnected from the server");
					break;
				}
				
				// message has successfully been received
				ASCIIEncoding encoder = new ASCIIEncoding();
				Logger.Debug(encoder.GetString(message, 0, bytesRead));
			
				string fileName = this.folder + "/" + System.Guid.NewGuid().ToString();

				if (message[0] != START_OF_BLOCK)
				{
					XmlTextReader reader = new XmlTextReader(new System.IO.StringReader(encoder.GetString(message, 0, bytesRead)));

		            while (reader.Read())
		            {
		                switch (reader.NodeType)
		                {
		                    case XmlNodeType.Element:
	                        while (reader.MoveToNextAttribute())
							{
								if (reader.Name == "xmlns" && reader.Value == "urn:hl7-org:v3")
								{
									hl7v3 = true;
								}
								if (reader.Name == "classCode" && reader.Value == "PAT")
								{
									writeToDisk = true;
								}
							}
							break;
		                }
		            }

					if (hl7v3)
					{
						fileName += ".v3.hl7";
						hl7v3 = true;
						writeToDisk = true;
					}
				}
				else
				{
					Logger.Debug("HL7 v2 message received.");
					fileName += ".v2.hl7";
					hl7v2 = true;
					
					// Check if it's a message that interests us
					
					// search for PID field
					int first = encoder.GetString(message, 0, bytesRead).IndexOf(PATIENT_ID);
			
					if (first != -1)
					{
						// Ok, the message has a patient id
						writeToDisk	= true;
					}
				}
								
				// Write message to disk
				if (writeToDisk)
				{
					try
					{
						StreamWriter outfile = new StreamWriter(fileName);
						outfile.Write(encoder.GetString(message, 0, bytesRead));
						outfile.Close();
					}
					catch (Exception e)
	    			{
						Logger.Fatal("Can't write message to disk: " + e.Message);
	    			}
					
					if (hl7v2)
					{
						// send a v2 ack
						
						string messageControlId = getMessageControlId(encoder.GetString(message, 0, bytesRead));
						
						string ackMsg = string.Empty;
						
						ackMsg += START_OF_BLOCK;
						ackMsg += "MSH|^~\\&|||||||ACK||P|2.2";
						ackMsg += CARRIAGE_RETURN;
						ackMsg += "MSA|AA|" + messageControlId;
						ackMsg += CARRIAGE_RETURN;
						ackMsg += END_OF_BLOCK;
						ackMsg += CARRIAGE_RETURN;
						
						Console.WriteLine("TCPHandler: Sending ack msg...");
									
						// Translate the passed message into ASCII and store it as a Byte array.
						Byte[] data = System.Text.Encoding.ASCII.GetBytes(ackMsg);
						
						try
						{
							clientStream.Write(data, 0, data.Length);
						}
						catch (Exception e)
	        			{
							Logger.Fatal("Can't send ack message: " + e.Message);
	        			}
					}
					else if (hl7v3)
					{
						// Send a v3 ack ¿?
					}
				}
			}
			
			tcpClient.Close();
		}				
	}
}

