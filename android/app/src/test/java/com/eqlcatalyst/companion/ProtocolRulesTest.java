package com.eqlcatalyst.companion;
import org.junit.Test;
import static org.junit.Assert.*;

public class ProtocolRulesTest {
    @Test public void privateAddressesOnly() { assertEquals("192.168.1.5", ProtocolRules.address("https://192.168.1.5:47831").getHost()); assertThrows(IllegalArgumentException.class, () -> ProtocolRules.address("https://8.8.8.8:47831")); }
    @Test public void noPlaintextOrSecretsInUrls() { for (String address : new String[] {"http://192.168.1.5:47831", "https://user@192.168.1.5:47831", "https://192.168.1.5:47831?token=x", "https://192.168.1.5:47831#pair=x"}) assertThrows(IllegalArgumentException.class, () -> ProtocolRules.address(address)); }
    @Test public void allPrivateRangesValidated() { assertNotNull(ProtocolRules.address("https://10.1.1.1:47831")); assertNotNull(ProtocolRules.address("https://172.31.1.1:47831")); assertThrows(IllegalArgumentException.class, () -> ProtocolRules.address("https://172.32.1.1:47831")); assertThrows(IllegalArgumentException.class, () -> ProtocolRules.address("https://192.168.300.1:47831")); }
    @Test public void discoveryCannotInventIdentity() { assertThrows(IllegalArgumentException.class, () -> ProtocolRules.serverHost("evil.example")); assertEquals("catalyst-0123456789abcdef0123456789abcdef.local", ProtocolRules.serverHost("0123456789abcdef0123456789abcdef")); }
    @Test public void wrongPinsRejected() throws Exception { byte[] certificate = new byte[] {1,2,3}; assertTrue(ProtocolRules.samePin(ProtocolRules.hash(certificate), certificate)); assertFalse(ProtocolRules.samePin("0".repeat(64), certificate)); assertFalse(ProtocolRules.samePin("", certificate)); }
    @Test public void incrementalRevisionMustMatch() { assertTrue(ProtocolRules.updateMatches(5,5)); assertFalse(ProtocolRules.updateMatches(5,4)); }
}
