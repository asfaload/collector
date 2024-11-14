import { createRemoteJWKSet, jwtVerify } from "jose";

export async function validateToken(
  token: string,
): Promise<Record<string, unknown>> {
  const jwks = createRemoteJWKSet(
    new URL("https://token.actions.githubusercontent.com/.well-known/jwks"),
  );
  const { payload } = await jwtVerify(token, jwks, {
    issuer: "https://token.actions.githubusercontent.com",
  });
  return payload;
}

if (Bun.argv.length < 3) {
  console.log("Pass jwt as argument to script")
  process.exit(1);
}
//let token = await Bun.file("token").text()
let token = Bun.argv[2];

let result = await validateToken(token);

console.log(JSON.stringify(result))
