using Godot;
using Jandirus.Core.Appearance;
using Jandirus.Core.Forms;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DO SOCO QUE TOCA UMA VEZ, E DO MACACO QUE DEITA (`--diagflick`).
///
/// As duas queixas do dono (2026-09-06) que moram no CLIENTE:
///   * *"quando ele soca, a animacao de socar dele fica loopando rapido por alguns segundos"* -- o
///     `flick("Attack")` do BYOND toca uma vez; aqui a pose `Atacando` dura a cadencia do golpe e o
///     corpo ficava preso no ciclo. Agora o soco toca uma vez e o corpo volta ao parado/andando ate a
///     pose mudar ou o proximo golpe chegar (`World.AoGolpe` -> `RestartState`).
///   * *"na oozaru ao ser nocauteado o sprite nao e rotacionado"* -- a folha do macaco tem quatro
///     quadros de KO e o cliente pegava o do SUL, uma bola que rodada fica igual. Agora pega o do
///     OESTE, desenhado como o quadro unico das outras folhas, e a mesma rotacao de todo mundo vale.
///
/// E, desde 2026-09-07, o GESTO DO KIAI (fase 4): a pose de tiro entra pelo `RestartState("blast")`,
/// o snapshot a segura e a pose normal a solta -- o `flick("Blast", usr)` de `Kiai.dm:16`.
///
/// Dois bonecos, sem janela e sem rede -- so o `CharacterVisual`, o catalogo e as folhas. O tempo
/// e o real: cada fase espera o relogio do corpo andar.
/// </summary>
public partial class RoboDoFlick : Node2D
{
	private CharacterVisual _base = null!, _fera = null!;
	private double _seg;
	private int _fase, _ok, _falhou;

	private void Checa(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _ok++; GD.Print($"[flick]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _falhou++; GD.PrintErr($"[flick]   FALHA {nome}   [{detalhe}]"); }
	}

	public override void _Ready()
	{
		GD.Print("[flick] ================ O SOCO TOCA UMA VEZ, E O MACACO DEITA (pedido do dono, 2026-09-06) ================");
		const string dados = "res://Assets/Data/visual.json";
		if (!Godot.FileAccess.FileExists(dados)) { Checa("PRECONDICAO: visual.json existe", false); Encerrar(); return; }
		VisualCatalog cat = VisualCatalog.Parse(Godot.FileAccess.GetFileAsString(dados));

		_base = new CharacterVisual { Name = "Base" };
		_fera = new CharacterVisual { Name = "Fera" };
		AddChild(_base);
		AddChild(_fera);
		_base.Vestir(cat, new Appearance { Cabelo = "Goku" }, "Saiyan", "Male");
		_fera.Vestir(cat, new Appearance { Cabelo = "Goku" }, "Saiyan", "Male");
		_fera.CorpoDaForma(CorpoDeForma.Oozaru);
		_base.SetMotion(Facing.South, false);
		_fera.SetMotion(Facing.South, false);

		Checa("PRECONDICAO: o boneco da fera veste a folha do macaco (camada propria da forma)",
			  _fera.AnimacaoDaFormaDeTeste.Length > 0, _fera.AnimacaoDaFormaDeTeste);
	}

	private void Encerrar()
	{
		GD.Print($"[flick] ================ {_ok} OK, {_falhou} FALHA(S) ================");
		_fase = 99;
		GetTree().Quit();
	}

	private static bool Comeca(string anim, string fam) => anim.StartsWith(fam, StringComparison.Ordinal);

	public override void _Process(double delta)
	{
		if (_fase == 99) return;
		_seg += delta;
		switch (_fase)
		{
			case 0:
			{
				// UM GOLPE: os dois bonecos entram no soco, esticado pra cadencia de 0,24 s.
				GD.Print("[flick] -- 1) o golpe entra, toca uma vez e o corpo volta ao parado --");
				_base.RestartState("attack", Protocol.AttackPoseMs / 1000.0);
				_fera.RestartState("attack", Protocol.AttackPoseMs / 1000.0);
				Checa("no instante do golpe o corpo base mostra o soco", Comeca(_base.AnimacaoDeTeste, "attack"), _base.AnimacaoDeTeste);
				Checa("...e o macaco tambem (a folha dele tem `attack`, 2 quadros de 0,2 s)", Comeca(_fera.AnimacaoDaFormaDeTeste, "attack"), _fera.AnimacaoDaFormaDeTeste);
				_fase = 1; _seg = 0;
				break;
			}
			case 1:
			{
				if (_seg < 0.6) return;   // bem depois dos 0,24 s do ciclo esticado
				Checa("0,6 s depois a POSE continua sendo o soco (e o que o servidor diz que o corpo faz)",
					  _base.EstadoDeTeste == "attack" && _fera.EstadoDeTeste == "attack", $"{_base.EstadoDeTeste}/{_fera.EstadoDeTeste}");
				Checa("...mas o soco JA TOCOU: o corpo base voltou ao parado", _base.FlickAcabouDeTeste && Comeca(_base.AnimacaoDeTeste, "default"), _base.AnimacaoDeTeste);
				Checa("...e o macaco tambem -- sem laco", _fera.FlickAcabouDeTeste && Comeca(_fera.AnimacaoDaFormaDeTeste, "default"), _fera.AnimacaoDaFormaDeTeste);

				// O SNAPSHOT REPETE A POSE (a cadencia ainda corre): nada recomeca.
				_base.SetPose(Protocol.Pose.Atacando);
				_fera.SetPose(Protocol.Pose.Atacando);
				Checa("um `SetPose(Atacando)` repetido (o snapshot de cada tique) NAO recomeca o soco", Comeca(_base.AnimacaoDeTeste, "default") && Comeca(_fera.AnimacaoDaFormaDeTeste, "default"),
					  $"{_base.AnimacaoDeTeste}/{_fera.AnimacaoDaFormaDeTeste}");

				// O PROXIMO GOLPE (o que `World.AoGolpe` faz a cada relato) recomeca o flick.
				_base.RestartState("attack", Protocol.AttackPoseMs / 1000.0);
				_fera.RestartState("attack", Protocol.AttackPoseMs / 1000.0);
				Checa("o golpe SEGUINTE (`RestartState`, o `flick` de cada soco) recomeca a animacao", Comeca(_base.AnimacaoDeTeste, "attack") && Comeca(_fera.AnimacaoDaFormaDeTeste, "attack") && !_base.FlickAcabouDeTeste,
					  $"{_base.AnimacaoDeTeste}/{_fera.AnimacaoDaFormaDeTeste}");
				_fase = 2; _seg = 0;
				break;
			}
			case 2:
			{
				if (_seg < 0.6) return;
				Checa("...e toca uma vez de novo", _base.FlickAcabouDeTeste && _fera.FlickAcabouDeTeste);
				_base.SetPose(Protocol.Pose.Normal);
				_fera.SetPose(Protocol.Pose.Normal);
				Checa("a pose que sai do soco leva o estado junto", _base.EstadoDeTeste == "default" && !_base.FlickAcabouDeTeste, _base.EstadoDeTeste);

				// O DEFEITO INJETADO: sem a regra, o soco fica em laco -- e o que o dono viu.
				GD.Print("[flick] -- 2) contra-exemplo: sem a regra do flick, o soco roda em laco --");
				CharacterVisual.SocoTocaUmaVezDeTeste = false;
				_fera.RestartState("attack", Protocol.AttackPoseMs / 1000.0);
				_fase = 3; _seg = 0;
				break;
			}
			case 3:
			{
				if (_seg < 0.6) return;
				Checa("[injecao] com a regra desligada, 0,6 s depois o macaco AINDA esta socando (o laco de antes)",
					  Comeca(_fera.AnimacaoDaFormaDeTeste, "attack") && !_fera.FlickAcabouDeTeste, _fera.AnimacaoDaFormaDeTeste);
				CharacterVisual.SocoTocaUmaVezDeTeste = true;
				_fera.SetPose(Protocol.Pose.Normal);

				// =====================================================================
				// 3) O KO DO MACACO DEITA COMO O DE TODO MUNDO
				// =====================================================================
				GD.Print("[flick] -- 3) o KO: o mesmo quadro deitado e a mesma rotacao pra todo corpo --");
				// (rumo, graus do corpo base, quadro do macaco, graus do macaco): os tres primeiros rumos sao
				// iguais pros dois; no OESTE o corpo de um quadro so da meia-volta (e fica de ponta-cabeca,
				// como sempre ficou) e o macaco, que tem o quadro espelhado, deita com ele SEM girar.
				foreach ((Facing olhar, float grausBase, string quadroFera, float grausFera) in new[]
				{
					(Facing.South, -90f, "ko_west", -90f), (Facing.North, 90f, "ko_west", 90f),
					(Facing.East, 0f, "ko_west", 0f), (Facing.West, 180f, "ko_east", 0f),
				})
				{
					_base.SetMotion(olhar, false); _fera.SetMotion(olhar, false);
					_base.SetPose(Protocol.Pose.Nocauteado); _fera.SetPose(Protocol.Pose.Nocauteado);
					_base.DeitarPor(olhar); _fera.DeitarPor(olhar);
					Checa($"olhando pro {olhar}: o corpo base deita com o quadro unico `ko` e {grausBase:0} graus",
						  _base.AnimacaoDeTeste == "ko" && Mathf.IsEqualApprox(_base.RotationDegrees, grausBase),
						  $"{_base.AnimacaoDeTeste} @ {_base.RotationDegrees:0}");
					Checa($"olhando pro {olhar}: o macaco deita com `{quadroFera}` a {grausFera:0} graus"
						  + (olhar == Facing.West ? " (o espelho, e nao a meia-volta que o punha de ponta-cabeca)" : " (os mesmos graus do corpo base)"),
						  _fera.AnimacaoDaFormaDeTeste == quadroFera && Mathf.IsEqualApprox(_fera.RotationDegrees, grausFera),
						  $"{_fera.AnimacaoDaFormaDeTeste} @ {_fera.RotationDegrees:0}");
					_base.SetPose(Protocol.Pose.Normal); _fera.SetPose(Protocol.Pose.Normal);
					_base.GirarPara(default); _fera.GirarPara(default);
				}
				Checa("de pe de novo, o macaco volta ao quadro parado da direcao em que olha",
					  Comeca(_fera.AnimacaoDaFormaDeTeste, "default") && Mathf.IsZeroApprox(_fera.RotationDegrees), _fera.AnimacaoDaFormaDeTeste);

				// =====================================================================
				// 4) O GESTO DO KIAI: A POSE DE TIRO, UMA VEZ (dono, 2026-09-07)
				// =====================================================================
				// O `flick("Blast", usr)` de `Kiai.dm:16`. No cliente sao duas metades: o `S2C.Gesto`
				// recomeca o desenho do quadro zero (`World.AoGesto` -> `RestartState("blast")`), e o
				// snapshot segura a pose por meio segundo (`Canalizando` + `CanalAtirando`, o mesmo
				// `blast` de quem solta um raio) e depois a solta. Aqui, sem rede, as duas metades sao
				// apertadas na mao -- e o que se mede e que a pose EXISTE na folha e VOLTA.
				GD.Print("[flick] -- 4) o gesto do Kiai: a pose de tiro entra, o snapshot a segura, a pose normal a solta --");
				_base.RestartState("blast");
				Checa("o gesto do Kiai poe o corpo na pose de tiro (`blast`, ou o soco que e o parente dela na folha sem `blast`)",
					  _base.EstadoDeTeste == "blast" && (Comeca(_base.AnimacaoDeTeste, "blast") || Comeca(_base.AnimacaoDeTeste, "attack")),
					  $"{_base.EstadoDeTeste}/{_base.AnimacaoDeTeste}");
				_base.SetPose(Protocol.Pose.Canalizando, canalAtirando: true);
				Checa("...e o snapshot com Canalizando + CanalAtirando a MANTEM (mesmo estado, nada recomeca)",
					  _base.EstadoDeTeste == "blast", _base.EstadoDeTeste);
				_base.SetPose(Protocol.Pose.Normal);
				Checa("...e a pose normal do snapshot seguinte a solta: o `flick` toca uma vez e o corpo volta",
					  _base.EstadoDeTeste == "default" && Comeca(_base.AnimacaoDeTeste, "default"), $"{_base.EstadoDeTeste}/{_base.AnimacaoDeTeste}");
				Checa("CONTRA-EXEMPLO: carregar um raio (Canalizando SEM atirar) nao veste a pose de tiro (`beams.dm:288-300`)",
					  (_base.SetPoseDeTeste(Protocol.Pose.Canalizando, false), _base.EstadoDeTeste).Item2 == "default", _base.EstadoDeTeste);
				Encerrar();
				break;
			}
		}
	}
}
