using System;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using PhantomVault.Core.Services.Network;

var tcp = new TcpClient("giblex.com", 443);
using var ssl = new SslStream(tcp.GetStream(), false, (_, _, _, _) => true);
await ssl.AuthenticateAsClientAsync("giblex.com");
var leaf = new X509Certificate2(ssl.RemoteCertificate!);
Console.WriteLine($"leaf => {SpkiPin.ComputePinBase64(leaf)}");

using var chain = new X509Chain();
chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
chain.Build(leaf);
foreach (var element in chain.ChainElements)
{
    var cert = element.Certificate;
    Console.WriteLine($"{cert.Subject} => {SpkiPin.ComputePinBase64(cert)}");
}
