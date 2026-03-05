import { Blaze, Core, HotWallet } from "@blaze-cardano/sdk";
import { wordlist } from "@blaze-cardano/core";
import { Blockfrost } from "@blaze-cardano/query";
import data from "../src/onchain/plutus.json" with { type: "json" };

const NETWORK = Core.NetworkId.Testnet;
const MNEMONIC =
  "oval bracket boss inquiry magic bottom jungle draw ripple mirror despair junk glass grunt minor desert hungry bracket feed hip lecture deal finish naive";
const BLOCKFROST_PROJECT_ID = "previewrpkjSMZh2AsH0qKcsVNlIiYUAjmtxJtv";
const DEFAULT_VALIDATOR_TITLE = "simple_dex_indexed.simple_dex_indexed.withdraw";

// Usage:
//   bun register-stake.ts <validator_title>                        (inline script)
//   bun register-stake.ts <validator_title> <txHash>#<txIndex>     (reference input)
const VALIDATOR_TITLE = process.argv[2] || DEFAULT_VALIDATOR_TITLE;
const SCRIPT_REF = process.argv[3]; // e.g. "abc123...#0"

const main = async () => {
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

  // Load validator from blueprint
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

  const credential = Core.Credential.fromCore({
    type: Core.CredentialType.ScriptHash,
    hash: scriptHash,
  });

  try {
    let txBuilder = blaze.newTransaction().addRegisterStake(credential);

    if (SCRIPT_REF) {
      // Use reference input — script already deployed on-chain
      const [txHash, txIndexStr] = SCRIPT_REF.split("#");
      const txIndex = parseInt(txIndexStr, 10);
      console.log(`Using reference input: ${txHash}#${txIndex}`);

      const refUtxo = await provider.resolveUnspentOutputs([
        new Core.TransactionInput(
          Core.TransactionId(txHash),
          BigInt(txIndex),
        ),
      ]);

      if (refUtxo.length === 0) {
        console.error("Reference UTxO not found on-chain");
        process.exit(1);
      }

      txBuilder = txBuilder.addReferenceInput(refUtxo[0]);
    } else {
      // Inline — attach full script to the transaction
      console.log("Using inline script (no reference input provided)");
      txBuilder = txBuilder.provideScript(script);
    }

    const tx = await txBuilder.complete();
    const signedTx = await blaze.signTransaction(tx);
    const txId = await blaze.provider.postTransactionToChain(signedTx);
    console.log("Stake registered! TxId:", txId);
  } catch (error: any) {
    if (
      error.message?.includes("re-register some already known credentials") ||
      error.message?.includes("StakeKeyRegisteredDELEG")
    ) {
      console.log("Stake already registered, skipping.");
    } else {
      console.error("Failed to register stake:", error.message);
      process.exit(1);
    }
  }
};

main();
