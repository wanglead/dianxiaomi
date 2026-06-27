import "./background.js";
import "./order-alert/native-bridge.js";

globalThis.OrderAlert.createNativeBridge(chrome);
