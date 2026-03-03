import { Blaze, Core, HotWallet } from "@blaze-cardano/sdk";
import { wordlist } from "@blaze-cardano/core";
import { Blockfrost } from "@blaze-cardano/query";
import data from "../src/onchain/plutus.json" with { type: "json" };

const NETWORK = Core.NetworkId.Testnet;
const MNEMONIC =
  "oval bracket boss inquiry magic bottom jungle draw ripple mirror despair junk glass grunt minor desert hungry bracket feed hip lecture deal finish naive";
const BLOCKFROST_PROJECT_ID = "previewrpkjSMZh2AsH0qKcsVNlIiYUAjmtxJtv";
const DEFAULT_VALIDATOR_TITLE = "simple_dex_withdraw.simple_dex_withdraw.withdraw";
const VALIDATOR_TITLE = process.argv[2] || DEFAULT_VALIDATOR_TITLE;

const main = async () => {
  // Setup provider and wallet
  const provider = new Blockfrost({
    network: "cardano-preview",
    projectId: BLOCKFROST_PROJECT_ID,
  });

  const seed = Core.mnemonicToEntropy(MNEMONIC, wordlist);
  const masterKey = Core.Bip32PrivateKey.fromBip39Entropy(
    Buffer.from(seed),
    "",
  );
  const wallet = await HotWallet.fromMasterkey(
    masterKey.hex(),
    provider,
    NETWORK,
  );
  const blaze = await Blaze.from(provider, wallet);
  const address = await wallet.getChangeAddress();

  console.log("Address:", address.toBech32());
  console.log("Balance:", (await wallet.getBalance()).coin());

  // Load the withdraw validator
  const validator = data.validators.find(
    (v) => v.title === VALIDATOR_TITLE,
  );
  if (!validator) {
    console.error(`Validator "${VALIDATOR_TITLE}" not found in plutus.json`);
    process.exit(1);
  }

  const script = Core.Script.newPlutusV3Script(
    new Core.PlutusV3Script(Core.HexBlob(validator.compiledCode)),
  );
  const scriptHash = script.hash();

  console.log("Script hash:", scriptHash);

  // Build stake credential
  const credential = Core.Credential.fromCore({
    type: Core.CredentialType.ScriptHash,
    hash: scriptHash,
  });

  // Register the staking credential
  try {
    const tx = await blaze
      .newTransaction()
      .addRegisterStake(credential)
      .provideScript(script)
      .complete();

    const signedTx = await blaze.signTransaction(tx);
    const txId = await blaze.provider.postTransactionToChain(signedTx);
    console.log("Stake registered! TxId:", txId);
  } catch (error: any) {
    if (
      error.message?.includes("re-register some already known credentials")
    ) {
      console.log("Stake already registered, skipping.");
    } else {
      console.error("Failed to register stake:", error.message);
      process.exit(1);
    }
  }
};

main();
