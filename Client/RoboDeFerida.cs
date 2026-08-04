using Godot;
using Jandirus.Core.Combat;

namespace Jandirus.Client;

/// <summary>
/// BANCADA DAS FERIDAS (`--diagferida`). Confere a cadeia inteira e tira uma FOTO.
///
/// ============================ O QUE SO O TESTE RESPONDE ============================
/// Um shader nao devolve nada: ele pinta e acaba. As perguntas que sobram, e que so um numero
/// (ou uma imagem) responde:
///   * a mascara sai do servidor e CHEGA ao cliente?
///   * a curva e a que o dono pediu -- roxo primeiro, sangue so no fim?
///   * as camadas certas recebem: corpo e roupa sim, cabelo/olhos/rabo nao?
///   * a caixa do quadro acompanha a animacao (senao a ferida escorre pelo corpo andando)?
/// ==================================================================================
///
/// COMO RODAR (com janela, pra a foto sair):
///     Godot --path . --host --feridateste --diagferida --nome Ferido --conta ferido
/// </summary>
public partial class RoboDeFerida : Node
{
	private double _t;
	private int _passo;
	private bool _acabou;

	private readonly List<string> _falhas = [];
	private readonly List<string> _passos = [];

	private static GameClient? C => GameClient.Instance;

	private void Conferir(bool ok, string oque)
	{
		_passos.Add((ok ? "  ok   " : "  FALHA") + "  " + oque);
		if (!ok) _falhas.Add(oque);
	}

	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (C is not { Connected: true } cli || World.Instancia is not { } mundo) return;

		_t += delta;
		if (_t < 0.6) return;
		_t = 0;

		switch (_passo++)
		{
			case 0:
				// A MASCARA VEM DO SERVIDOR. Se este passo cair, o resto e desenho de mentira: o
				// cliente estaria inventando ferida em vez de mostrar a que o servidor calculou.
				if (!cli.Feridas.ContainsKey(cli.LocalId)) { _passo = 0; return; }
				Conferir(true, "a mascara de feridas chega do servidor");
				break;

			case 1:
			{
				MascaraDeFeridas m = cli.Feridas[cli.LocalId];
				_passos.Add("         " + m);

				// A ESCADA DO `--feridateste`, degrau a degrau. Sao os numeros da curva, e nao
				// "parece certo": cabeca 85% de vida, bracos 60, torso 40, abdomen 22, pernas 15.
				Conferir(m.Hematoma(ZonaDeFerida.Cabeca) == 0 && m.Sangue(ZonaDeFerida.Cabeca) == 0,
					"cabeca quase inteira (85% de vida): NADA aparece");

				// A ZONA FICA COM O PIOR MEMBRO DELA, e o teste arranca o braco direito -- entao a
				// regiao dos bracos vai ao maximo mesmo com o esquerdo em 60% de vida. Isso e a
				// regra, e nao um efeito colateral: um braco arrancado nao vira "braco meio ruim".
				Conferir(m.Hematoma(ZonaDeFerida.Bracos) >= 0.99 && m.Sangue(ZonaDeFerida.Bracos) >= 0.99,
					"bracos: um foi ARRANCADO, entao a regiao vai ao maximo (o pior membro manda)");

				Conferir(m.Hematoma(ZonaDeFerida.Torso) >= 0.99 && m.Sangue(ZonaDeFerida.Torso) > 0,
					$"torso feio (40%): roxo cheio E sangue comecando ({m.Sangue(ZonaDeFerida.Torso):0.00})");

				Conferir(m.Sangue(ZonaDeFerida.Abdomen) > m.Sangue(ZonaDeFerida.Torso),
					"abdomen (22%) sangra mais que o torso (40%)");

				Conferir(m.Sangue(ZonaDeFerida.Pernas) > m.Sangue(ZonaDeFerida.Abdomen),
					$"pernas no limite (15%): o maior sangramento ({m.Sangue(ZonaDeFerida.Pernas):0.00})");

				// A ORDEM DAS FASES, que e o pedido do dono em uma linha: nao existe regiao que
				// sangre sem estar roxa antes.
				bool ordem = true;
				for (int i = 0; i < MascaraDeFeridas.Zonas; i++)
				{
					var z = (ZonaDeFerida)i;
					if (m.Sangue(z) > 0 && m.Hematoma(z) < 0.99) ordem = false;
				}
				Conferir(ordem, "NENHUMA regiao sangra sem o roxo estar cheio (o roxo vem primeiro)");

				// A FASE DO ROXO, medida na CURVA e nao na escada.
				//
				// Na escada acima nao sobra regiao pra ela: as unicas moderadas tem membro
				// arrancado, que leva tudo ao maximo. Entao a fase e conferida direto na funcao --
				// e assim ela continua sendo conferida mesmo que a escada mude de valores.
				var corpo = Body.Novo();
				foreach (BodyPart bp in corpo.Partes) bp.Vida = bp.VidaMax * 0.60;
				MascaraDeFeridas so60 = Jandirus.Core.Combat.Feridas.De(corpo);
				bool soRoxo = true;
				for (int i = 0; i < MascaraDeFeridas.Zonas; i++)
				{
					var z = (ZonaDeFerida)i;
					if (so60.Hematoma(z) <= 0 || so60.Sangue(z) != 0) soRoxo = false;
				}
				Conferir(soRoxo,
					$"a 60% de vida o corpo TODO fica SO roxo, sem uma gota (hema {so60.Hematoma(ZonaDeFerida.Torso):0.00})");

				foreach (BodyPart bp in corpo.Partes) bp.Vida = bp.VidaMax * 0.95;
				Conferir(Jandirus.Core.Combat.Feridas.De(corpo).Limpa,
					"a 95% de vida o corpo fica LIMPO (arranhao nao deixa marca)");

				// ARRANCADO SOME. E so braco e perna tem bit: nucleo arrancado mata, entao nao ha
				// corpo andando sem ele pra desenhar.
				Conferir(m.Perdeu(MascaraDeFeridas.Membro.BracoDir)
					  && m.Perdeu(MascaraDeFeridas.Membro.PernaEsq),
					"o braco direito e a perna esquerda vem marcados como ARRANCADOS");
				Conferir(!m.Perdeu(MascaraDeFeridas.Membro.BracoEsq)
					  && !m.Perdeu(MascaraDeFeridas.Membro.PernaDir),
					"os membros do outro lado NAO vem marcados (a amputacao distingue lado)");
				break;
			}

			case 2:
			{
				// AS CAMADAS CERTAS. Cabelo, olhos e rabo tem que continuar em modo 0.
				CharacterVisual? v = mundo.VisualLocalDeTeste;
				if (v == null) { Conferir(false, "achei o CharacterVisual local"); break; }

				(int corpo, int roupas, int outras) = v.ModosDeTeste();
				Conferir(corpo == 1, $"o CORPO esta em modo PELE ({corpo})");
				Conferir(outras == 0, $"cabelo, olhos e rabo ficam de fora ({outras} camada(s) fora do modo 0)");
				_passos.Add($"         camadas de roupa em modo PANO: {roupas}");
				break;
			}

			case 3:
			{
				// A CAIXA DO QUADRO. Se ela nao acompanhar a animacao, as faixas do corpo ficam
				// travadas no primeiro quadro e a ferida escorre pelo boneco enquanto ele anda.
				CharacterVisual? v = mundo.VisualLocalDeTeste;
				Conferir(v != null && v.CaixaDeTeste() is { } cx && cx.Max > cx.Min,
					"a caixa do quadro chega ao shader (as faixas do corpo dependem dela)");
				break;
			}

			case 4:
				Fotografar("user://feridas.png");
				break;

			case 5:
				// DERRUBA O PROPRIO BONECO. O host e admin, e o verb aceita um id -- o meu.
				// E o unico jeito de por o corpo na pose de NOCAUTE num teste: ela e um desenho
				// DEITADO, e era nela que as faixas mediam o eixo errado.
				cli.SendVerbo("admin_kb", cli.LocalId.ToString());
				break;

			case 6:
			case 7:
				break;   // deixa a pose trocar e o corpo assentar

			case 8:
			{
				CharacterVisual? v = mundo.VisualLocalDeTeste;
				Conferir(v != null && v.DeitadoDeTeste,
					"caido: o shader foi avisado de que o desenho esta DEITADO");
				Fotografar("user://feridas-ko.png");
				break;
			}

			default:
				_acabou = true;
				GD.Print("\n[ferida] ===== BANCADA DAS FERIDAS =====");
				foreach (string l in _passos) GD.Print("[ferida] " + l);
				GD.Print(_falhas.Count == 0
					? "[ferida] ===== TUDO OK ====="
					: $"[ferida] ===== {_falhas.Count} FALHA(S) =====\n[ferida]   " + string.Join("\n[ferida]   ", _falhas));
				break;
		}
	}

	/// <summary>Salva a tela, se houver renderizador. No headless o `GetImage` volta vazio, e tudo bem.</summary>
	private void Fotografar(string destino)
	{
		try
		{
			Image? img = GetViewport()?.GetTexture()?.GetImage();
			if (img == null || img.IsEmpty()) { _passos.Add("  --     sem foto (headless nao renderiza)"); return; }
			string caminho = ProjectSettings.GlobalizePath(destino);
			img.SavePng(caminho);
			_passos.Add("  ok     foto salva em " + caminho);
		}
		catch (Exception e) { _passos.Add("  --     sem foto: " + e.Message); }
	}
}
