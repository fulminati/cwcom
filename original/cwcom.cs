//tabs=4
//-----------------------------------------------------------------------------
// TITLE:		cwcom.cs
//
// FACILITY:	RSS-Fed Morse Code News Robot 
//
// ABSTRACT:	Contains classes that implement the CWCom internet protocol.
//				Designed to be used together with the message wrapper classes
//				in cwmessages.cs.
//
// ENVIRONMENT:	Microsoft.NET 2.0/3.5
//				Developed under Visual Studio.NET 2008
//
//
// AUTHOR:		Bob Denny, <rdenny@dc3.com>
//
// Edit Log:
//
// When			Who		What
//----------	---		-------------------------------------------------------
// xx-Jan-10	rbd		Initial edits
// 04-Feb-10	rbd		0.5.1
// 04-Feb-10	rbd		0.5.2 - Make thread safe, as needed for Service Control 
//						Manager shutdown (and just to be sure anyway). Change
//						from calling Console.Log to calling via MessageLogger
//						delegate that client passes in constructor.
// 10-Feb-10	rbd		0.6.1 - Log connect only if not a reconnect.
// 01-Mar-10	rbd		0.6.4 - Split delay for word space befor and after
//						sending code group that starts with a space.
// 22-Mar-10	rbd		0.6.6 - Add receive timeout to Connect() to prevent 
//						missing an ack from hanging this forever!
// 01-Apr-10	rbd		0.6.9 - SF artifact 2980615 Create new UdpClient before 
//						reconnect.
// 05-Apr-10	rbd		0.7.2 - Add "blind" parameter to Connect() for periodic
//						reconnect during transmissions, to avoid delay if ACK
//						not received.
// 05-Apr-10	rbd		0.7.2 - Add real receive loop, no more blind connect.
// 07-Apr-10	rbd		0.7.2 - No more local TxStatus, client determines.
// 09-Apr-10	rbd		0.7.2 - Fix 30 second delay on reconnect receiver.
// 29-Sep-10	rbd		0.8.1 - Make reconnect delay grow exponentially up
//						to 1920 sec. Catch UDP connect failures and log.
// 12-Oct-10	rbd		0.8.2 - Fix exponential reconnect delay. Log UDP receive
//						failures unless disconnecting.
// 13-Oct-10	rbd		0.8.3 - Don't log "CancelBlockingCall" receive errors.
//-----------------------------------------------------------------------------
//

using System;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace com.dc3.cwcom
{
	//
	// Implements a CWCom connection to a server or a CWCom
	//
	public class CwCom
	{
		private UdpClient _udp = null;
		private object _objLock;
		private IPEndPoint _remIP;
		private string _remHost;
		private int _remPort;
		private short _channel;
		private string _ident;
		private DateTime _lastAckTime;
		private IdentMessage _idMsg;
		private DataMessage _dataMsg;
		private int _seqNo;
		private DateTime _nextTxIdent = DateTime.MinValue;
		private Thread _receiverThread = null;
		private MessageReceiver _receiver; 
		private MessageLogger _logger;
		private bool _disconnecting = false;
		private const int _minReconnSecs = 30;
		private const int _maxReconnSecs = 64 * _minReconnSecs;
		private int _reconnSecs = _minReconnSecs;

		public delegate void MessageReceiver(byte[] rcvMsg);
		public delegate void MessageLogger(string msg);							// Client's logging function must be like this

		public CwCom(MessageReceiver receiver, MessageLogger logger)
		{
			_receiver = receiver;
			_logger = logger;
			_objLock = new object();
			_remHost = "localhost";
			_remPort = 7860;
			_channel = 1000;
			_ident = "NoID";
			_seqNo = 3;
			_disconnecting = false;

			_idMsg = new IdentMessage();
			_dataMsg = new DataMessage();
		}

		public void Connect(string Host, int Port, short Channel, string Ident, bool Blind)
		{
			bool justCon;
			int ticks;

			lock (_objLock)
			{
				_remHost = Host;
				_remPort = Port;
				_channel = Channel;
				_ident = Ident;
				_idMsg.ID = _ident;
				_dataMsg.ID = _ident;

				while (!_disconnecting)											// Loop forever, till server reappears
				{
					//
					// This is in the loop to handle changes in the Ionosphere
					// server's dynamic address. If the connect fails, we'll close
					// the UDP socket and reopen it via DNS domain name, hopefully
					// jumping us to the new address. Oh, and the initial connect is
					// done here too!
					//
					if (_udp == null)
					{
						_logger("Opening UDP");
						_udp = new UdpClient();
						try
						{
							_udp.Connect(_remHost, _remPort);
						}
						catch (Exception e)
						{
							_udp = null;
							_logger("UDP connect fail: " + e.Message);
							_logger("Wait " + _reconnSecs + ", then try again...");
							ticks = _reconnSecs;
							if (_reconnSecs < _maxReconnSecs) _reconnSecs *= 2;
							while (!_disconnecting && ticks > 0) 				// Wait _reconnSecs before reconnect
							{
								Thread.Sleep(1000);
								ticks--;
							}
							if (_disconnecting)
								break;
							else
								continue;
						}
						_receiverThread = new Thread(new ThreadStart(ReceiverThread));
						_receiverThread.Name = "Receiver thread";
						_receiverThread.Start();
						justCon = true;
						Thread.Sleep(2000);										// Important for reliability
					}
					else
						justCon = false;
					//
					// Connect and wait for ack
					//
					if (justCon) _logger("Sending connect message...");
					_udp.Send(new CtrlMessage(CtrlMessage.MessageTypes.Connect, _channel).Packet, CtrlMessage.Length);
					if (Blind) break;											// Blind connect, bail out now.
					for (int i = 0; i < 20; i++)								// Up to 2 sec at 10Hz
					{
						if (_disconnecting) return;
						if (_lastAckTime.AddSeconds(5) > DateTime.Now)			// If got a recent ack
						{
							if (justCon) _logger("Connected to " + _remIP.Address.ToString());
							_reconnSecs = _minReconnSecs;						// Finally, reset reconn interval
							return;												// GOOD!
						}
						Thread.Sleep(100);										// Wait then try again
					}
					_logger("No ACK from server. Close, wait " + _reconnSecs + ", then reopen...");
					_udp.Close();												// This will cause ReceiverThread to exit
					_receiverThread.Join(1000);
					_udp = null;
					ticks = _reconnSecs;
					if (_reconnSecs < _maxReconnSecs) _reconnSecs *= 2;
					while (!_disconnecting && ticks > 0) 						// Wait _reconnSecs before reconnect
					{
						Thread.Sleep(1000);
						ticks--;
					}

				}
			}
		}

		public void Disconnect()
		{
			lock (_objLock)
			{
				if (_udp == null) return;
				_udp.Send(new CtrlMessage(CtrlMessage.MessageTypes.Disconnect, _channel).Packet, CtrlMessage.Length);
				Thread.Sleep(100);
				_udp.Send(new CtrlMessage(CtrlMessage.MessageTypes.Disconnect, _channel).Packet, CtrlMessage.Length);
				Thread.Sleep(200);
				_disconnecting = true;
				_udp.Close();
				_udp = null;
				_receiverThread.Join(1000);
			}
		}

		private void ReceiverThread()
		{
			byte[] recvBuf;
			_lastAckTime = DateTime.MinValue;
			_logger("Receiver thread starting");
			while (true)
			{
				try { recvBuf = _udp.Receive(ref _remIP); }
				catch (SocketException ex)
				{
					if (!_disconnecting && !ex.Message.Contains("CancelBlockingCall"))
						_logger("**Receive failed: " + ex.Message);
					break; 
				}
				if (recvBuf.Length == CtrlMessage.Length - 2)					// This is an ack!
					_lastAckTime = DateTime.Now;
				else if (_receiver != null)
					_receiver(recvBuf);
			}
			_logger("Receiver thread exited");
		}

		public void Identify(string Text)
		{
			lock (_objLock)
			{
				if (_udp == null) return;
				_idMsg.SequenceNo = _seqNo++;
				_idMsg.Text = Text;
				_udp.Send(_idMsg.Packet, IdentMessage.Length);
				_udp.Send(_idMsg.Packet, IdentMessage.Length);
			}
		}

		public void SendCode(Int32[] Code, string Text, string TxStatus)
		{
			lock (_objLock)
			{
				if (_udp == null) return;
				if (DateTime.Now >= _nextTxIdent)							// Don't water tx down with needless idents!
				{
					Identify(TxStatus);
					_nextTxIdent = DateTime.Now.AddSeconds(10);
				}

				int iStart = 0;
				int splitWait = 0;

				//
				// Pacing/timing here is a bit tricky. To avoid a noticeably long
				// pause between the first and second letters of a word (following 
				// a space), the beginning delay is split 50/50 before and after
				// sending the code group. This results in much better timing at
				// the receive end without things getting ahead.
				//
				if (Text.StartsWith(" "))
				{
					splitWait = Math.Abs(Code[0] / 2);
					Thread.Sleep(splitWait);
					iStart = 1;
				}

				_dataMsg.SequenceNo = _seqNo++;
				_dataMsg.Code = Code;
				_dataMsg.Text = Text;
				_udp.Send(_dataMsg.Packet, DataMessage.Length);
				_udp.Send(_dataMsg.Packet, DataMessage.Length);

				if (iStart == 1)
					Thread.Sleep(splitWait);

				int delay = 0;
				for (int i = iStart; i < Code.Length; i++)
					delay += Math.Abs(Code[i]);
				Thread.Sleep(delay); //Convert.ToInt32(0.95F * delay));
			}
		}
	}
}
