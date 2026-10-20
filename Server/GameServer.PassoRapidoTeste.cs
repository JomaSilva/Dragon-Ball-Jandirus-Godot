using Godot;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// ============================ O PASSO PUXADO QUE ATRAVESSAVA O MURO (`--passorapidoteste`) ============================
/// O dono (2026-10-19): *"esses lags causam problemas de sync com o server as vezes, fazendo personagem atravessar
/// parede etc"*. O caminho do defeito: um quadro longo no cliente vira um passo grande demais; o `ValidateStep` o
/// recusa pela VELOCIDADE e devolve o ponto mais longe que o orcamento alcanca, na direcao pedida -- e esse ponto saia
/// sem a pergunta da parede, e o `AplicarInput` o gravava. Com o muro no caminho, o corpo ia parar dentro dele (ou do
/// outro lado).
///
/// A REGUA E A FUNCAO DE PRODUCAO, num mapa feito aqui: `ZoneCollision.Montar` com um muro de um tile de largura, e o
/// `MoveRules.ValidateStep` com mapa -- a MESMA chamada que o `AplicarInput` faz com o mapa da zona e o orcamento do
/// jogador. Um mapa de verdade traria o acaso de onde ha muro fino (ver as armadilhas de palco das bancadas); este tem
/// o muro onde a conta diz.
///
///     Godot --headless --path . --server --port 9702 --passorapidoteste
/// ===================================================================================================================
/// </summary>
public sealed partial class GameServer
{
	private int _passoOk, _passoFalhou;
	private bool _passoRapidoJaRodou;

	private void AfirmarPasso(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _passoOk++; GD.Print($"[passorapido]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _passoFalhou++; GD.PrintErr($"[passorapido]   FALHA {nome}   [{detalhe}]"); }
	}

	// O PALCO: 40 x 12 celulas, o muro na coluna 20 de cima a baixo. O corpo anda na fileira 6, com a caixa dos pes
	// inteira dentro dela (centro em y = 6*32 + 8: os pes ficam de 203 a 213, a fileira vai de 192 a 224).
	private const int LarguraDoPalco = 40, AlturaDoPalco = 12, ColunaDoMuro = 20;

	private static readonly float YDoCorpo = 6 * ZoneCollision.TileSize + 8;

	/// <summary>O centro de um corpo de pe NO MEIO da celula `cx` da fileira do palco.</summary>
	private static Vec2 NaCelula(int cx) => new(cx * ZoneCollision.TileSize + ZoneCollision.TileSize / 2f, YDoCorpo);

	/// <summary>
	/// Velocidade 10 (1.600 px/s): o orcamento cheio do servidor e 216 px -- mais que o muro de 32 px mais a caixa dos
	/// pes. E a corrida de um corpo treinado, e e o que torna o puxao capaz de atravessar.
	/// </summary>
	private const float VelocidadeDoPalco = 10f;

	private static ZoneCollision PalcoDoMuro()
	{
		var bits = new byte[(LarguraDoPalco * AlturaDoPalco + 7) / 8];
		for (int cy = 0; cy < AlturaDoPalco; cy++)
		{
			int i = cy * LarguraDoPalco + ColunaDoMuro;
			bits[i >> 3] |= (byte)(1 << (i & 7));
		}
		return ZoneCollision.Montar(LarguraDoPalco, AlturaDoPalco, bits);
	}

	/// <summary>
	/// UM PASSO PEDIDO, como o `AplicarInput` o valida: o quadro longo (dt no teto de 0,25 s) e o orcamento do jogador
	/// partindo de zero -- ele enche ate o teto nessa chamada mesma, que e o estado de quem acabou de engasgar.
	/// </summary>
	private static (bool Aceito, Vec2 Fica) Passo(ZoneCollision mapa, Vec2 de, Vec2 pedido, float orcamento = 0f)
	{
		bool aceito = MoveRules.ValidateStep(de, pedido, MoveRules.MaxDeltaSeconds, VelocidadeDoPalco, mapa,
											 ref orcamento, out Vec2 fica);
		return (aceito, fica);
	}

	public void RodarBancadaDoPassoRapido()
	{
		if (_passoRapidoJaRodou) return;
		_passoRapidoJaRodou = true;
		_passoOk = _passoFalhou = 0;
		GD.Print("[passorapido] ================ O PASSO PUXADO NAO ATRAVESSA O MURO (dono, 2026-10-19) ================");
		try
		{
			ZoneCollision mapa = PalcoDoMuro();
			float muroDe = ColunaDoMuro * ZoneCollision.TileSize, muroAte = muroDe + ZoneCollision.TileSize;
			AfirmarPasso("PREMISSA: o palco tem o muro na coluna 20 e chao livre dos dois lados",
				mapa.BlockedCell(ColunaDoMuro, 6) && !mapa.BlockedCell(ColunaDoMuro - 1, 6) && !mapa.BlockedCell(ColunaDoMuro + 1, 6)
				&& !MoveRules.Occupied(mapa, NaCelula(ColunaDoMuro - 1)) && !MoveRules.Occupied(mapa, NaCelula(ColunaDoMuro + 1)));

			// ---------------- 1. O CONTROLE: em campo aberto o puxao continua andando ----------------
			GD.Print("[passorapido] --- 1) passo rapido demais em CAMPO ABERTO: o corpo e puxado ate o orcamento (nao volta pra tras) ---");
			Vec2 aberto = NaCelula(12);
			(bool aceitoAberto, Vec2 ficaAberto) = Passo(mapa, aberto, aberto + new Vec2(-600, 0));
			AfirmarPasso("o servidor RECUSA o passo de 600 px num quadro (passa do orcamento de 216 px)", !aceitoAberto);
			AfirmarPasso("...e o corpo ANDA o orcamento na direcao pedida -- recusar nao e mandar de volta pra onde estava",
				ficaAberto.X < aberto.X - 150 && ficaAberto.X > aberto.X - 230, $"de {aberto.X:0} para {ficaAberto.X:0}");

			// ---------------- 2. O DEFEITO: o puxao que cruzaria o muro fica onde estava ----------------
			GD.Print("[passorapido] --- 2) passo rapido demais CONTRA O MURO: o puxao nao entra nem atravessa ---");
			Vec2 encostado = NaCelula(ColunaDoMuro - 1);
			Vec2 pedido = encostado + new Vec2(600, 0);
			(bool aceitoMuro, Vec2 ficaMuro) = Passo(mapa, encostado, pedido);
			AfirmarPasso("o servidor recusa o passo rapido que atravessaria o muro", !aceitoMuro);
			AfirmarPasso("**O CORPO FICA DO LADO DE CA**: nem dentro do muro, nem do outro lado",
				ficaMuro.X + MoveRules.BodyHalfW <= muroDe && !MoveRules.Occupied(mapa, ficaMuro)
				&& !MoveRules.PathOccupied(mapa, encostado, ficaMuro),
				$"de {encostado.X:0} para {ficaMuro.X:0} (o muro vai de {muroDe:0} a {muroAte:0})");

			MoveRules.PassoRapidoAtravessaParedeDeTeste = true;
			Vec2 ficaComDefeito;
			try { (_, ficaComDefeito) = Passo(mapa, encostado, pedido); }
			finally { MoveRules.PassoRapidoAtravessaParedeDeTeste = false; }
			AfirmarPasso("(defeito injetado: o puxao sem a pergunta da parede) o corpo vai parar do OUTRO LADO do muro, e a regua de cima REPROVA",
				!(ficaComDefeito.X + MoveRules.BodyHalfW <= muroDe && !MoveRules.PathOccupied(mapa, encostado, ficaComDefeito)),
				$"de {encostado.X:0} para {ficaComDefeito.X:0}");

			// ---------------- 3. O QUE JA ERA: o passo de velocidade honesta contra o muro ----------------
			GD.Print("[passorapido] --- 3) passo de velocidade HONESTA contra o muro: continua barrado como sempre ---");
			(bool aceitoHonesto, Vec2 ficaHonesto) = Passo(mapa, encostado, encostado + new Vec2(64, 0), orcamento: 1000f);
			AfirmarPasso("o passo de 64 px dentro do orcamento, atravessando o muro, e recusado e o corpo fica onde estava",
				!aceitoHonesto && ficaHonesto.X == encostado.X, $"para {ficaHonesto.X:0}");

			// ---------------- 4. QUEM JA ESTA PRESO NAO FICA PRESO ----------------
			GD.Print("[passorapido] --- 4) o corpo JA DENTRO do muro (empurrado): o passo rapido pra fora continua saindo ---");
			Vec2 preso = NaCelula(ColunaDoMuro);
			(_, Vec2 ficaPreso) = Passo(mapa, preso, preso + new Vec2(-600, 0));
			AfirmarPasso("PREMISSA: o corpo esta dentro do muro", MoveRules.Occupied(mapa, preso));
			AfirmarPasso("...e o puxao pra fora anda o orcamento, como antes -- a pergunta nova e so pra quem esta em chao livre",
				ficaPreso.X < preso.X - 150, $"de {preso.X:0} para {ficaPreso.X:0}");
		}
		catch (Exception e)
		{
			AfirmarPasso("o roteiro correu ate o fim sem excecao", false, $"{e.GetType().Name}: {e.Message}");
			GD.PrintErr(e.ToString());
		}
		finally
		{
			MoveRules.PassoRapidoAtravessaParedeDeTeste = false;
			GD.Print($"[passorapido] ================ {_passoOk} OK, {_passoFalhou} FALHA(S) ================");
		}
	}
}
