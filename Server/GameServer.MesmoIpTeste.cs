using Godot;
using Jandirus.Core.Social;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--mesmoipteste` (2026-10-27) -- o pedido 110 do dono: *"personagens com mesmo ip n conseguiriam fazer nada entre eles, nem gerar
/// raiva, nem dar bp, nem dar unlocked, nem zenkai absolutamente nada"*. Roda no BOOT, com corpos de PESSOA forjados e um endereco de
/// mentira pra cada (`IpDeTeste`), e mede cada funil pela porta de producao, com o mesmo IP e com outro (o controle):
///   M1  a LUTA: o golpe entre dois do mesmo IP rende zero (os dois lados); com outro IP, rende;
///   M2  a DERROTA: cair diante de um do mesmo IP nao solta o Zenkai; diante de outro IP, solta;
///   M3  o LUTO: quem assiste o amigo cair nao se enfurece se o amigo e da conexao dele; a outra testemunha, sim;
///   M4  LIBERAR O POTENCIAL de outro (o ritual): recusado no mesmo IP, e o BP nao mexe; com outro IP, libera;
///   M5  a LICAO e o MESTRE: ensinar uma skill e aceitar um mestre sao recusados no mesmo IP, com a frase;
///   M6  o SAQUE: revistar um caido do mesmo IP e recusado, com a frase;
///   M7  a LINGUA: oferecer a lingua nativa a quem esta na frente e recusado no mesmo IP, e nenhuma licao fica pendente.
///
///     Godot --headless --path . --server --port 11771 --mesmoipteste
/// </summary>
public sealed partial class GameServer
{
	private int _miOk, _miFalhou;
	private const int IdBaseDoMesmoIpDeTeste = 94_000;

	private void AfirmarMi(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _miOk++; GD.Print($"[mesmoip]   OK    {oque}   [{detalhe}]"); return; }
		_miFalhou++;
		GD.PrintErr($"[mesmoip]   FALHA {oque}   {detalhe}");
	}

	public void RodarBancadaDoMesmoIp()
	{
		_miOk = _miFalhou = 0;
		GD.Print("[mesmoip] ================ DO MESMO IP NADA RENDE (dono, 2026-10-27) ================");

		ZoneKey zona = ZoneKey.Premade("Earth");
		var corpos = new List<ServerPlayer>();
		ServerPlayer Forjar(int i, string nome, string ip, string raca = "Human", float tilesX = 0)
		{
			var p = new ServerPlayer
			{
				Id = IdBaseDoMesmoIpDeTeste + i, Peer = null, Name = nome, Race = raca, Genero = "Male", Idade = 25, Zone = zona,
				Pos = new Vec2(tilesX * ZoneCollision.TileSize, 0), Conta = $"bancada_mesmoip_{i}", Slot = 0,
				Ficha = new Jandirus.Core.Stats.Fighter { Race = raca, BP = 10_000 }, Livro = new Jandirus.Core.Skills.SkillBook(),
			};
			p.Ficha.Class = "Normal";
			PorNoMundo(p);
			IpDeTeste[p.Id] = ip;
			corpos.Add(p);
			return p;
		}

		MesmoIpNaBancadaDeTeste = true;
		try
		{
			// ---- M1) a luta
			ServerPlayer a = Forjar(1, "bancada: o principal", "10.0.0.1");
			ServerPlayer alt = Forjar(2, "bancada: o alt", "10.0.0.1", tilesX: 1);
			ServerPlayer outro = Forjar(3, "bancada: o outro", "10.0.0.2", tilesX: 2);
			RegistrarAtaque(alt, a);
			RegistrarAtaque(a, alt);
			RegistrarAtaque(outro, a);
			double comOAlt = FatorDaLuta(a, alt), doAlt = FatorDaLuta(alt, a), comOOutro = FatorDaLuta(a, outro);
			AfirmarMi("M1. a LUTA entre dois do mesmo IP rende ZERO, dos dois lados; com outro IP, rende",
					  comOAlt == 0 && doAlt == 0 && comOOutro > 0, $"com o alt {comOAlt}, do alt {doAlt}, com o outro {comOOutro}");

			// ---- M2) a derrota e o Zenkai
			ServerPlayer sai = Forjar(4, "bancada: o saiyajin", "10.0.0.3", "Saiyan", 3);
			ServerPlayer altDoSai = Forjar(5, "bancada: o alt do saiyajin", "10.0.0.3", tilesX: 4);
			sai.Ficha.zenkaiGuardado = 1_000_000;
			sai.Combate.Nocautear(30);
			AoPerderALuta(sai, altDoSai, morreu: false);
			double depoisDoAlt = sai.Ficha.zenkaiGuardado;
			AoPerderALuta(sai, outro, morreu: false);
			double depoisDoOutro = sai.Ficha.zenkaiGuardado;
			sai.Combate.Levantar();
			AfirmarMi("M2. cair diante de um do MESMO IP nao solta o Zenkai; diante de outro IP, solta",
					  depoisDoAlt == 1_000_000 && depoisDoOutro < 1_000_000, $"banco: {depoisDoAlt:0} -> {depoisDoOutro:0}");

			// ---- M3) o luto de quem assiste
			ServerPlayer vitima = Forjar(6, "bancada: a vitima", "10.0.0.4", tilesX: 10);
			ServerPlayer testemunhaDaConexao = Forjar(7, "bancada: a testemunha da mesma conexao", "10.0.0.4", tilesX: 11);
			ServerPlayer testemunhaDeFora = Forjar(8, "bancada: a testemunha de fora", "10.0.0.5", tilesX: 12);
			ServerPlayer algoz = Forjar(9, "bancada: o algoz", "10.0.0.6", tilesX: 13);
			DeclararDeTeste(testemunhaDaConexao, vitima, Relacao.MuitoBom);
			DeclararDeTeste(testemunhaDeFora, vitima, Relacao.MuitoBom);
			testemunhaDaConexao.FuriaExtremaAte = testemunhaDeFora.FuriaExtremaAte = 0;
			testemunhaDaConexao.RaivaComumAte = testemunhaDeFora.RaivaComumAte = 0;
			vitima.Combate.Nocautear(30);
			AoPerderALuta(vitima, algoz, morreu: false);
			long agora = NowMs();
			bool daConexao = testemunhaDaConexao.RaivaComumAte > agora || testemunhaDaConexao.FuriaExtremaAte > agora;
			bool deFora = testemunhaDeFora.RaivaComumAte > agora || testemunhaDeFora.FuriaExtremaAte > agora;
			vitima.Combate.Levantar();
			AfirmarMi("M3. o LUTO: quem assiste o amigo cair NAO se enfurece se o amigo e da conexao dele; a testemunha de fora, sim",
					  !daConexao && deFora, $"da conexao {daConexao}, de fora {deFora}");

			// ---- M4) liberar o potencial de outro
			ServerPlayer kaioshin = Forjar(10, "bancada: o kaioshin", "10.0.0.7", tilesX: 20);
			ServerPlayer protegido = Forjar(11, "bancada: o protegido da conexao", "10.0.0.7", tilesX: 21);
			ServerPlayer visitante = Forjar(12, "bancada: o visitante", "10.0.0.8", tilesX: 22);
			protegido.Ficha.hiddenpotential = visitante.Ficha.hiddenpotential = 50_000;
			double bpProtegido = protegido.Ficha.BP, bpVisitante = visitante.Ficha.BP;
			List<string> recusa = Ouvir(() => LiberarOPotencial(protegido, kaioshin, 1, autor: kaioshin.Name, comRecarga: false, exigeBanco: false));
			bool liberouOVisitante = LiberarOPotencial(visitante, kaioshin, 1, autor: kaioshin.Name, comRecarga: false, exigeBanco: false);
			AfirmarMi("M4. LIBERAR O POTENCIAL de outro e recusado no MESMO IP (o BP nao mexe, e a frase sai); com outro IP, libera",
					  protegido.Ficha.BP == bpProtegido && recusa.Any(s => s.Contains("mesma conexão")) && liberouOVisitante && visitante.Ficha.BP > bpVisitante,
					  $"protegido {bpProtegido:0} -> {protegido.Ficha.BP:0}; visitante {bpVisitante:0} -> {visitante.Ficha.BP:0}; {string.Join(" | ", recusa)}");

			// ---- M5) a licao e o mestre
			List<string> licao = Ouvir(() => OferecerLicao(a, "/datum/skill/general/Fly", alt));
			alt.PedidoDoMestre = new PedidoDeMestre(true, a.Assinatura, a.Name, "forma-que-nao-existe", NowMs() + 60_000);
			List<string> vinculo = Ouvir(() => ResponderAoMestre(alt, aceitou: true));
			outro.PedidoDoMestre = new PedidoDeMestre(true, a.Assinatura, a.Name, "forma-que-nao-existe", NowMs() + 60_000);
			List<string> vinculoDeFora = Ouvir(() => ResponderAoMestre(outro, aceitou: true));
			AfirmarMi("M5. a LICAO e o MESTRE sao recusados no MESMO IP, com a frase; o mestre de outro IP nao ouve essa frase",
					  licao.Any(s => s.Contains("mesma conexão")) && vinculo.Any(s => s.Contains("mesma conexão"))
					  && !vinculoDeFora.Any(s => s.Contains("mesma conexão")),
					  $"licao: {string.Join(" | ", licao)} || mestre: {string.Join(" | ", vinculo)} || de fora: {string.Join(" | ", vinculoDeFora)}");

			string tecnica = PorQueNaoEnsinaATecnica(a, alt, 999, out _, conferirRecarga: false);
			AfirmarMi("M5b. ...e a TECNICA INVENTADA tambem nao se ensina no mesmo IP", tecnica.Contains("mesma conexão"), tecnica);

			// ---- M6) o saque
			alt.Pos = a.Pos;
			alt.Combate.Nocautear(30);
			List<string> saque = Ouvir(() => Saquear(a, ""));
			alt.Combate.Levantar();
			AfirmarMi("M6. revistar um caido do MESMO IP e recusado, com a frase", saque.Any(s => s.Contains("mesma conexão")), string.Join(" | ", saque));

			// ---- M7) a lingua
			ServerPlayer professor = Forjar(13, "bancada: o saiyajin que ensina", "10.0.0.9", "Saiyan", 30);
			ServerPlayer irmao = Forjar(14, "bancada: o humano da mesma conexao", "10.0.0.9", tilesX: 31);
			professor.Facing = Facing.East;
			irmao.Pos = professor.Pos + new Vec2(ZoneCollision.TileSize, 0);
			List<string> lingua = Ouvir(() => UsarVerboDeLingua(professor, VerboDaLinguaEnsinar));
			bool pendente = _licaoDeLingua.ContainsKey(irmao.Id);
			_licaoDeLingua.Remove(irmao.Id);
			AfirmarMi("M7. a LINGUA nao se ensina no mesmo IP: a oferta e recusada com a frase, e nenhuma licao fica pendente",
					  lingua.Any(s => s.Contains("mesma conexão")) && !pendente, $"{string.Join(" | ", lingua)}; pendente {pendente}");
		}
		finally
		{
			MesmoIpNaBancadaDeTeste = false;
			foreach (ServerPlayer p in corpos)
			{
				IpDeTeste.Remove(p.Id);
				_players.Remove(p.Id);
				ZoneList(zona.Hash).Remove(p);
			}
		}
		GD.Print($"[mesmoip] ================ {_miOk} passaram, {_miFalhou} falharam ================");
	}
}
