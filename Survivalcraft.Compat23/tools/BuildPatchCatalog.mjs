import { createHash } from "node:crypto";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import process from "node:process";
import { fileURLToPath } from "node:url";
import { deflateRawSync, inflateRawSync } from "node:zlib";

const samples = [
    ["01-gun-mod-2.8", "枪械Mod2.8_修复.scmod"],
    ["02-siyah-akrep-2.8", "SiyahAkrep黑蝎2.8_修复.scmod"],
    ["03-mmap-1.0", "MMap-纸质地图1.0_修复.scmod"],
    ["06-dayz-2.2.4", "DayZ2.2.4_修复.scmod"],
    ["07-scplus-6.0.3", "扩展6.0.3_修复.scmod"]
];

const headingCode = Buffer.from("有头有脸天才少年,耍猴表演敢为人先", "utf8");
const headingCode2 = Buffer.from("修改他人mod请获得原作者授权，否则小心出名！", "utf8");
const deltaMagic = Buffer.from("SC23DLT1", "ascii");

function startsWith(buffer, prefix) {
    return buffer.length >= prefix.length && buffer.subarray(0, prefix.length).equals(prefix);
}

function decipherScmod(buffer) {
    if (startsWith(buffer, headingCode)) {
        return Buffer.from(buffer.subarray(headingCode.length)).reverse();
    }
    if (startsWith(buffer, headingCode2)) {
        const payload = buffer.subarray(headingCode2.length);
        const firstHalfLength = Math.ceil(payload.length / 2);
        const result = Buffer.alloc(payload.length);
        let evenOffset = 0;
        let oddOffset = firstHalfLength;
        for (let i = 0; i < result.length; i++) {
            if ((i & 1) === 0) {
                result[i] = payload[evenOffset++];
            } else {
                result[i] = payload[oddOffset++];
            }
        }
        return result;
    }
    return buffer;
}

function extractZip(buffer) {
    let endRecord = -1;
    const searchStart = Math.max(0, buffer.length - 65557);
    for (let offset = buffer.length - 22; offset >= searchStart; offset--) {
        if (buffer.readUInt32LE(offset) === 0x06054b50
            && offset + 22 + buffer.readUInt16LE(offset + 20) <= buffer.length) {
            endRecord = offset;
            break;
        }
    }
    if (endRecord < 0) {
        throw new Error("ZIP end record is missing");
    }
    const entriesCount = buffer.readUInt16LE(endRecord + 10);
    let centralOffset = buffer.readUInt32LE(endRecord + 16);
    const files = new Map();
    for (let entryIndex = 0; entryIndex < entriesCount; entryIndex++) {
        if (buffer.readUInt32LE(centralOffset) !== 0x02014b50) {
            throw new Error("Invalid ZIP central directory record");
        }
        const method = buffer.readUInt16LE(centralOffset + 10);
        const compressedSize = buffer.readUInt32LE(centralOffset + 20);
        const uncompressedSize = buffer.readUInt32LE(centralOffset + 24);
        const fileNameLength = buffer.readUInt16LE(centralOffset + 28);
        const extraLength = buffer.readUInt16LE(centralOffset + 30);
        const commentLength = buffer.readUInt16LE(centralOffset + 32);
        const localOffset = buffer.readUInt32LE(centralOffset + 42);
        const nameStart = centralOffset + 46;
        const fileName = buffer.toString("utf8", nameStart, nameStart + fileNameLength).replace(/\\/g, "/");
        const localNameLength = buffer.readUInt16LE(localOffset + 26);
        const localExtraLength = buffer.readUInt16LE(localOffset + 28);
        const dataOffset = localOffset + 30 + localNameLength + localExtraLength;
        const compressed = buffer.subarray(dataOffset, dataOffset + compressedSize);
        let contents;
        if (method === 0) {
            contents = Buffer.from(compressed);
        } else if (method === 8) {
            contents = inflateRawSync(compressed);
        } else {
            throw new Error("Unsupported ZIP compression method " + method + " for " + fileName);
        }
        if (contents.length !== uncompressedSize) {
            throw new Error("Uncompressed size mismatch for " + fileName);
        }
        if (!fileName.endsWith("/")) {
            files.set(fileName, contents);
        }
        centralOffset += 46 + fileNameLength + extraLength + commentLength;
    }
    return files;
}

function sha256(buffer) {
    return createHash("sha256").update(buffer).digest("hex");
}

function encodeCommands(source, target) {
    const keySize = 12;
    const minCopyLength = 16;
    const maxOffsetsPerKey = 24;
    const positions = new Map();
    for (let offset = 0; offset <= source.length - keySize; offset++) {
        const key = source.toString("latin1", offset, offset + keySize);
        let offsets = positions.get(key);
        if (!offsets) {
            offsets = [];
            positions.set(key, offsets);
        }
        if (offsets.length < maxOffsetsPerKey) {
            offsets.push(offset);
        } else if ((offset & 127) === 0) {
            offsets[offsets.length - 1] = offset;
        }
    }

    const commands = [];
    let literalStart = 0;
    let targetOffset = 0;
    while (targetOffset <= target.length - keySize) {
        const key = target.toString("latin1", targetOffset, targetOffset + keySize);
        const candidates = positions.get(key);
        let bestSourceOffset = -1;
        let bestLength = 0;
        if (candidates) {
            for (const sourceOffset of candidates) {
                let length = keySize;
                const maxLength = Math.min(source.length - sourceOffset, target.length - targetOffset);
                while (length < maxLength && source[sourceOffset + length] === target[targetOffset + length]) {
                    length++;
                }
                if (length > bestLength) {
                    bestLength = length;
                    bestSourceOffset = sourceOffset;
                }
            }
        }
        if (bestLength >= minCopyLength) {
            if (targetOffset > literalStart) {
                const literal = target.subarray(literalStart, targetOffset);
                const command = Buffer.allocUnsafe(5 + literal.length);
                command.writeUInt8(1, 0);
                command.writeInt32LE(literal.length, 1);
                literal.copy(command, 5);
                commands.push(command);
            }
            const copy = Buffer.allocUnsafe(9);
            copy.writeUInt8(0, 0);
            copy.writeInt32LE(bestSourceOffset, 1);
            copy.writeInt32LE(bestLength, 5);
            commands.push(copy);
            targetOffset += bestLength;
            literalStart = targetOffset;
        } else {
            targetOffset++;
        }
    }
    if (literalStart < target.length) {
        const literal = target.subarray(literalStart);
        const command = Buffer.allocUnsafe(5 + literal.length);
        command.writeUInt8(1, 0);
        command.writeInt32LE(literal.length, 1);
        literal.copy(command, 5);
        commands.push(command);
    }

    return Buffer.concat(commands);
}

function makeDelta(source, target) {
    if (source.length > 0x7fffffff || target.length > 0x7fffffff) {
        throw new Error("Compat23 delta format currently supports files up to 2 GiB");
    }
    const rawCommands = encodeCommands(source, target);
    const compressedCommands = deflateRawSync(rawCommands, { level: 9 });
    const header = Buffer.alloc(20);
    deltaMagic.copy(header, 0);
    header.writeInt32LE(source.length, 8);
    header.writeInt32LE(target.length, 12);
    header.writeInt32LE(compressedCommands.length, 16);
    return Buffer.concat([header, compressedCommands]);
}

function applyDelta(source, delta) {
    if (!delta.subarray(0, deltaMagic.length).equals(deltaMagic)) {
        throw new Error("Invalid generated delta magic");
    }
    const sourceLength = delta.readInt32LE(8);
    const targetLength = delta.readInt32LE(12);
    const compressedLength = delta.readInt32LE(16);
    if (sourceLength !== source.length || delta.length !== 20 + compressedLength) {
        throw new Error("Invalid generated delta lengths");
    }
    const commands = inflateRawSync(delta.subarray(20));
    const target = Buffer.alloc(targetLength);
    let commandOffset = 0;
    let targetOffset = 0;
    while (targetOffset < target.length) {
        const opcode = commands.readUInt8(commandOffset++);
        if (opcode === 0) {
            const sourceOffset = commands.readInt32LE(commandOffset);
            const length = commands.readInt32LE(commandOffset + 4);
            commandOffset += 8;
            if (sourceOffset < 0 || length <= 0 || sourceOffset + length > source.length
                || targetOffset + length > target.length) {
                throw new Error("Invalid generated copy command");
            }
            source.copy(target, targetOffset, sourceOffset, sourceOffset + length);
            targetOffset += length;
        } else if (opcode === 1) {
            const length = commands.readInt32LE(commandOffset);
            commandOffset += 4;
            if (length <= 0 || targetOffset + length > target.length
                || commandOffset + length > commands.length) {
                throw new Error("Invalid generated literal command");
            }
            commands.copy(target, targetOffset, commandOffset, commandOffset + length);
            commandOffset += length;
            targetOffset += length;
        } else {
            throw new Error("Invalid generated delta opcode " + opcode);
        }
    }
    if (commandOffset !== commands.length) {
        throw new Error("Generated delta contains trailing commands");
    }
    return target;
}

function bestPatch(source, target) {
    const delta = makeDelta(source, target);
    if (!applyDelta(source, delta).equals(target)) {
        throw new Error("Generated delta did not reconstruct its target");
    }
    const compressedReplacementSize = deflateRawSync(target, { level: 9 }).length;
    if (delta.length < compressedReplacementSize) {
        return { kind: "BinaryDelta", bytes: delta };
    }
    return { kind: "Replace", bytes: target };
}

export async function generatePatchCatalog(auditRoot, outputRoot) {
    const rules = [];
    const report = [];
    for (const [sampleId, targetName] of samples) {
        const sampleRoot = path.join(auditRoot, sampleId);
        const sourceArchive = decipherScmod(await readFile(path.join(sampleRoot, "original.scmod")));
        const targetArchive = decipherScmod(await readFile(path.join(auditRoot, "repaired", targetName)));
        const sourceFiles = extractZip(sourceArchive);
        const targetFiles = extractZip(targetArchive);
        const sourceInfo = JSON.parse(sourceFiles.get("modinfo.json").toString("utf8").replace(/^\uFEFF/, ""));
        const targetInfo = JSON.parse(targetFiles.get("modinfo.json").toString("utf8").replace(/^\uFEFF/, ""));
        if (sourceInfo.PackageName !== targetInfo.PackageName || sourceInfo.Version !== targetInfo.Version) {
            throw new Error("Package identity changed in sample " + sampleId);
        }

        const changedFiles = [];
        for (const [fileName, sourceBytes] of sourceFiles) {
            if (targetFiles.has(fileName)) {
                continue;
            }
            changedFiles.push({
                Path: fileName,
                Kind: "Delete",
                SourceSize: sourceBytes.length,
                SourceSha256: sha256(sourceBytes),
                PatchSize: 0,
                PatchSha256: "",
                TargetSize: 0,
                TargetSha256: "",
                Optional: false
            });
            report.push({
                sample: sampleId,
                file: fileName,
                kind: "Delete",
                sourceBytes: sourceBytes.length,
                targetBytes: 0,
                payloadBytes: 0,
                fullCompressedBytes: 0
            });
        }

        for (const [fileName, targetBytes] of targetFiles) {
            const sourceBytes = sourceFiles.get(fileName);
            if (!sourceBytes) {
                throw new Error("New target file needs an add operation: " + sampleId + "/" + fileName);
            }
            if (sourceBytes.equals(targetBytes)) {
                continue;
            }

            const patch = bestPatch(sourceBytes, targetBytes);
            const suffix = patch.kind === "BinaryDelta" ? ".scdelta" : ".bin";
            const patchRelativePath = "patches/" + sampleId + "/" + fileName + suffix;
            const patchName = "PatchData/" + patchRelativePath;
            const outputPath = path.join(outputRoot, ...patchRelativePath.split("/"));
            await mkdir(path.dirname(outputPath), { recursive: true });
            await writeFile(outputPath, patch.bytes);

            changedFiles.push({
                Path: fileName,
                Kind: patch.kind,
                PatchFile: patchName,
                SourceSize: sourceBytes.length,
                SourceSha256: sha256(sourceBytes),
                PatchSize: patch.bytes.length,
                PatchSha256: sha256(patch.bytes),
                TargetSize: targetBytes.length,
                TargetSha256: sha256(targetBytes),
                Optional: fileName.toLowerCase().endsWith(".pdb")
            });
            report.push({
                sample: sampleId,
                file: fileName,
                kind: patch.kind,
                sourceBytes: sourceBytes.length,
                targetBytes: targetBytes.length,
                payloadBytes: patch.bytes.length,
                fullCompressedBytes: deflateRawSync(targetBytes, { level: 9 }).length
            });
        }
        rules.push({
            RuleId: sampleId,
            PackageName: sourceInfo.PackageName,
            ModVersion: sourceInfo.Version,
            Files: changedFiles
        });
    }

    const catalog = { FormatVersion: 1, TargetScVersion: "2.4.0.0", Rules: rules };
    await mkdir(outputRoot, { recursive: true });
    await writeFile(path.join(outputRoot, "catalog.json"), JSON.stringify(catalog, null, 2) + "\n", "utf8");
    return report;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    const [auditRoot, outputRoot] = process.argv.slice(2);
    if (!auditRoot || !outputRoot) {
        console.error("Usage: node BuildPatchCatalog.mjs <community-mod-repair> <PatchData-output>");
        process.exitCode = 2;
    } else {
        const report = await generatePatchCatalog(auditRoot, outputRoot);
        for (const entry of report) {
            console.log(entry.sample + " | " + entry.file + " | " + entry.kind
                + " | " + entry.payloadBytes + " / " + entry.fullCompressedBytes + " bytes");
        }
    }
}
